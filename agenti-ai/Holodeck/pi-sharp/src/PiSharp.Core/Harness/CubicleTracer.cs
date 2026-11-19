// Turns AgentEvents into cubicle trace events and ships them in the background. See docs adr-pi-sharp-cubicle-client.
// Bounded queue (drop-oldest), batches of FlushSize or FlushInterval, every text field truncated client-side,
// an unavailable server drops the batch silently. OnEvent never throws.
// The web cubicle view (ai-factory kb 1.0.21) renders from these payloads: tool args and details go as JSON
// objects so it can show commands, paths and edit diffs; strings are cut at MaxText (chat keeps 16000).
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using PiSharp.Core.Agent;
using PiSharp.Core.Messages;

namespace PiSharp.Core.Harness;

public sealed class CubicleTracer : IAsyncDisposable
{
    public const int MaxText = 16000;
    /// <summary>A structured payload (args, details) larger than this once serialized is not sent whole.</summary>
    public const int MaxStructured = 48000;
    /// <summary>Items kept per JSON array inside a structured payload.</summary>
    public const int MaxArrayItems = 200;
    public const int MaxRunId = 80;
    public const int DefaultCapacity = 1000;
    public const int DefaultFlushSize = 50;
    public static readonly TimeSpan DefaultFlushInterval = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan DisposeBound = TimeSpan.FromSeconds(5);

    private readonly CubicleClient _client;
    private readonly IReadOnlyList<string> _about;
    /// <summary>One <c>cache_miss</c> line per assistant turn (adr-pi-sharp-cache-stats). The callback runs
    /// on the tracing path and returns the formatted line; null = off.</summary>
    private readonly Func<string>? _cacheMissLine;
    /// <summary>The task the seat works on (factory: <c>chat-&lt;message id&gt;</c>); null = no effort counting.</summary>
    private readonly Func<string?>? _taskKey;
    /// <summary>The work item the harness bound the current task to (the waking message's about slug), or null.</summary>
    private readonly Func<string?>? _workItem;
    /// <summary>The context gauge at each assistant turn: estimated tokens, window, compaction trigger.</summary>
    private readonly Func<ContextGauge>? _context;
    private readonly TaskEffort _effort = new();
    private readonly int _flushSize;
    private readonly TimeSpan _flushInterval;
    private readonly Channel<JsonObject> _queue;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _pump;
    private readonly Dictionary<string, long> _toolStarts = new(StringComparer.Ordinal);

    private string _runId = NewRunId();
    private long _seq;
    private int _droppedInRun;
    private int _droppedTotal;
    private int _failedBatches;
    private int _sentBatches;
    private bool _disposed;

    public CubicleTracer(
        CubicleClient client,
        IReadOnlyList<string>? about = null,
        int capacity = DefaultCapacity,
        int flushSize = DefaultFlushSize,
        TimeSpan? flushInterval = null,
        Func<string>? cacheMissLine = null,
        Func<string?>? taskKey = null,
        Func<string?>? workItem = null,
        Func<ContextGauge>? context = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _about = about ?? [];
        _cacheMissLine = cacheMissLine;
        _taskKey = taskKey;
        _workItem = workItem;
        _context = context;
        _flushSize = Math.Clamp(flushSize, 1, 200);
        _flushInterval = flushInterval ?? DefaultFlushInterval;
        _queue = Channel.CreateBounded<JsonObject>(
            new BoundedChannelOptions(Math.Max(1, capacity))
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            },
            _ =>
            {
                Interlocked.Increment(ref _droppedInRun);
                Interlocked.Increment(ref _droppedTotal);
            });
        _pump = Task.Run(PumpAsync);
    }

    /// <summary>Events dropped because the queue was full (process lifetime).</summary>
    public int DroppedEvents => Volatile.Read(ref _droppedTotal);

    /// <summary>Batches the server did not accept (non-2xx, timeout, connection failure).</summary>
    public int FailedBatches => Volatile.Read(ref _failedBatches);

    /// <summary>Batches accepted with a 2xx.</summary>
    public int SentBatches => Volatile.Read(ref _sentBatches);

    public string RunId => _runId;

    public void OnEvent(AgentEvent evt)
    {
        if (evt is null || _disposed) return;
        try
        {
            TrackTask();
            if (evt is TurnEnd) _effort.Pause();
            switch (evt)
            {
                case AgentStart:
                    _runId = NewRunId();
                    _seq = 0;
                    Interlocked.Exchange(ref _droppedInRun, 0);
                    _toolStarts.Clear();
                    _effort.Active();
                    break;
                case MessageEnd { Message: UserMessage user }:
                    Enqueue("prompt", new JsonObject { ["text"] = Truncate(user.Text) });
                    break;
                case MessageEnd { Message: AssistantMessage assistant }:
                    _effort.Assistant(assistant.Usage, assistant.ResponseModel ?? assistant.Model);
                    var payload = AssistantPayload(assistant);
                    if (_context?.Invoke() is { } gauge) payload["context"] = gauge.ToJson();
                    Enqueue("assistant", payload);
                    if (_cacheMissLine is { } line) Enqueue("cache_miss", new JsonObject { ["line"] = line() });
                    break;
                case ToolExecutionStart start:
                    _effort.Bind(TaskEffort.WorkItemOf(start.ToolName, start.Arguments), authoritative: false);
                    _toolStarts[start.ToolCallId] = Stopwatch.GetTimestamp();
                    Enqueue("tool_start", new JsonObject
                    {
                        ["name"] = start.ToolName,
                        ["tool_call_id"] = start.ToolCallId,
                        ["args"] = Structured(start.Arguments),
                    });
                    break;
                case ToolExecutionDeadline deadline:
                    Enqueue("tool_deadline", new JsonObject
                    {
                        ["name"] = deadline.ToolName,
                        ["tool_call_id"] = deadline.ToolCallId,
                        ["deadline_ms"] = (long)deadline.Deadline.TotalMilliseconds,
                    });
                    break;
                case TurnStart:
                    _effort.Active();
                    Enqueue("turn_start", new JsonObject());
                    break;
                case ToolExecutionEnd end:
                    _effort.ToolEnd(end.IsError);
                    Enqueue("tool_end", ToolEndPayload(end));
                    break;
                case AgentEnd end:
                    Enqueue("run_end", RunEndPayload(end));
                    EnqueueEffort(_effort.Close("run_end"));
                    break;
                case TurnEnd { Final: true, ToolResults.Count: > 0 } ended:
                    // The run ended on a tool batch (the loop guard, or tools that terminate) and now waits for a
                    // follow-up message; AgentEnd comes only after one. Without this the last trace is tool_end and
                    // the KB's RESUME sweep reads the stopped seat as mid-turn (team watch 2026-10-03, pi4).
                    Enqueue("run_end", new JsonObject
                    {
                        ["reason"] = "tool_batch",
                        ["tools"] = new JsonArray([.. ended.ToolResults.Select(r => (JsonNode?)r.ToolName)]),
                        ["dropped"] = Volatile.Read(ref _droppedInRun),
                    });
                    EnqueueEffort(_effort.Close("tool_batch"));
                    break;
                case TurnEnd { Final: true }:
                    // An attached seat's run stays open across many wakes (AgentEnd is rare there), so the counts close at
                    // every final turn; a follow-up on the same task adds a further delta.
                    EnqueueEffort(_effort.Close("turn_end"));
                    break;
                default:
                    break; // MessageStart/MessageUpdate/ToolExecutionUpdate/TurnEnd are noise for the cubicle.
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Tracing must never disturb the agent.
        }
    }

    /// <summary>Follows the seat's current task: another task taking the seat closes the old one's counts first, and
    /// the harness's work-item binding attaches to the task it was made for.</summary>
    private void TrackTask()
    {
        if (_taskKey is null) return;
        var task = _taskKey();
        EnqueueEffort(_effort.Enter(task));
        if (task is not null) _effort.Bind(_workItem?.Invoke(), authoritative: true);
    }

    private void EnqueueEffort(JsonObject? delta)
    {
        if (delta is null) return;
        Enqueue("effort", delta, delta["work_item"]?.GetValue<string>());
    }

    private void Enqueue(string type, JsonObject payload, string? workItem = null)
    {
        var evt = new JsonObject
        {
            ["seq"] = ++_seq,
            ["run_id"] = _runId,
            ["at"] = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            ["type"] = type,
            ["payload"] = payload,
        };
        // An effort delta names the work item it was closed for; every other event the one the open task is bound to.
        workItem ??= type == "effort" ? null : _effort.WorkItem;
        if (_about.Count > 0 || workItem is not null)
        {
            var about = new JsonArray();
            foreach (var a in _about) about.Add((JsonNode?)JsonValue.Create(a));
            if (workItem is not null && !_about.Contains(workItem, StringComparer.Ordinal))
                about.Add((JsonNode?)JsonValue.Create(workItem));
            evt["about"] = about;
        }
        _queue.Writer.TryWrite(evt);
    }

    private static JsonObject AssistantPayload(AssistantMessage assistant)
    {
        var toolCalls = new JsonArray();
        foreach (var call in assistant.ToolCalls) toolCalls.Add((JsonNode?)JsonValue.Create(call.Name));
        var thinkingChars = 0;
        var thinkingText = new System.Text.StringBuilder();
        foreach (var part in assistant.Content)
        {
            if (part is not ThinkingContent thinking) continue;
            thinkingChars += thinking.Thinking.Length;
            if (thinking.Redacted || thinking.Thinking.Length == 0) continue;
            if (thinkingText.Length > 0) thinkingText.Append("\n\n");
            thinkingText.Append(thinking.Thinking);
        }
        var payload = new JsonObject
        {
            ["text"] = Truncate(assistant.Text),
            ["thinking_chars"] = thinkingChars,
            ["tool_calls"] = toolCalls,
            ["stop_reason"] = StopReasonName(assistant.StopReason),
            ["usage"] = UsagePayload(assistant.Usage),
            ["model"] = assistant.ResponseModel ?? assistant.Model,
        };
        if (thinkingText.Length > 0) payload["thinking"] = Truncate(thinkingText.ToString());
        if (!string.IsNullOrEmpty(assistant.ErrorMessage)) payload["error"] = Truncate(assistant.ErrorMessage);
        return payload;
    }

    private JsonObject ToolEndPayload(ToolExecutionEnd end)
    {
        var payload = new JsonObject
        {
            ["name"] = end.ToolName,
            ["tool_call_id"] = end.ToolCallId,
            ["is_error"] = end.IsError,
        };
        if (_toolStarts.Remove(end.ToolCallId, out var started))
            payload["duration_ms"] = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var text = string.Concat(end.Result.Content.OfType<TextContent>().Select(t => t.Text));
        // Client-side tracer arm of the response-size set: report the runtime string length
        // before truncation so truncated results are measurable. This is the runtime-reported
        // size of what the tool returned to the client — not the wire bytes of the response.
        payload["result_bytes"] = text.Length;
        payload["result_truncated"] = text.Length > MaxText;
        payload[end.IsError ? "error" : "result"] = Truncate(text);
        // Structured result for rendering (edit: diff, patch, firstChangedLine); never part of what the model saw.
        if (end.Result.Details is { } details) payload["details"] = Structured(details);
        return payload;
    }

    private JsonObject RunEndPayload(AgentEnd end)
    {
        var usage = Usage.Zero;
        var turns = 0;
        AssistantMessage? last = null;
        foreach (var message in end.Messages)
        {
            if (message is not AssistantMessage assistant) continue;
            turns++;
            usage = usage.Add(assistant.Usage);
            last = assistant;
        }
        return new JsonObject
        {
            ["reason"] = last is null ? "empty" : StopReasonName(last.StopReason),
            ["turns"] = turns,
            ["usage"] = UsagePayload(usage),
            ["dropped"] = Volatile.Read(ref _droppedInRun),
        };
    }

    private static JsonObject UsagePayload(Usage usage) => new()
    {
        ["input"] = usage.Input,
        ["output"] = usage.Output,
        ["cache_read"] = usage.CacheRead,
        ["cache_write"] = usage.CacheWrite,
        ["total"] = usage.TotalTokens,
        ["reasoning"] = usage.Reasoning,
    };

    private static string StopReasonName(StopReason reason) => reason switch
    {
        StopReason.Pending => "pending",
        StopReason.Stop => "stop",
        StopReason.Length => "length",
        StopReason.ToolUse => "toolUse",
        StopReason.Error => "error",
        StopReason.Aborted => "aborted",
        _ => reason.ToString(),
    };

    /// <summary>
    /// A deep copy of a structured payload with every string cut at <see cref="MaxText"/> and every array at
    /// <see cref="MaxArrayItems"/>. If the copy still serializes past <see cref="MaxStructured"/> chars it is
    /// replaced by <c>{"omitted_chars": n}</c> so one tool cannot crowd a batch out of the server's limits.
    /// </summary>
    public static JsonNode? Structured(JsonNode? node)
    {
        var copy = Cut(node, 0);
        if (copy is null) return null;
        var length = copy.ToJsonString().Length;
        return length <= MaxStructured ? copy : new JsonObject { ["omitted_chars"] = length };
    }

    private static JsonNode? Cut(JsonNode? node, int depth)
    {
        switch (node)
        {
            case null:
                return null;
            case JsonObject obj:
            {
                var copy = new JsonObject();
                if (depth >= 16) return copy;
                foreach (var (key, value) in obj) copy[key] = Cut(value, depth + 1);
                return copy;
            }
            case JsonArray array:
            {
                var copy = new JsonArray();
                if (depth >= 16) return copy;
                foreach (var item in array.Take(MaxArrayItems)) copy.Add(Cut(item, depth + 1));
                return copy;
            }
            case JsonValue value when value.TryGetValue<string>(out var text):
                return JsonValue.Create(Truncate(text));
            default:
                return JsonNode.Parse(node.ToJsonString());
        }
    }

    internal static string Truncate(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Length <= MaxText ? text : text[..MaxText];
    }

    private static string NewRunId()
    {
        var id = DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N");
        return id.Length <= MaxRunId ? id : id[..MaxRunId];
    }

    private async Task PumpAsync()
    {
        var reader = _queue.Reader;
        var batch = new List<JsonObject>(_flushSize);
        try
        {
            while (await reader.WaitToReadAsync(_stop.Token).ConfigureAwait(false))
            {
                Fill(reader, batch);
                if (batch.Count < _flushSize)
                {
                    using var window = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                    window.CancelAfter(_flushInterval);
                    try
                    {
                        while (batch.Count < _flushSize && await reader.WaitToReadAsync(window.Token).ConfigureAwait(false))
                            Fill(reader, batch);
                    }
                    catch (OperationCanceledException) when (!_stop.IsCancellationRequested)
                    {
                        // Flush window elapsed; ship what we have.
                    }
                }
                await SendAsync(batch).ConfigureAwait(false);
                batch.Clear();
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped; whatever is left is dropped.
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Never propagate.
        }
    }

    private void Fill(ChannelReader<JsonObject> reader, List<JsonObject> batch)
    {
        while (batch.Count < _flushSize && reader.TryRead(out var item)) batch.Add(item);
    }

    private async Task SendAsync(List<JsonObject> batch)
    {
        if (batch.Count == 0) return;
        var array = new JsonArray();
        foreach (var item in batch) array.Add((JsonNode)item);
        var ok = await _client.PostAsync(array, _stop.Token).ConfigureAwait(false);
        if (ok) Interlocked.Increment(ref _sentBatches);
        else Interlocked.Increment(ref _failedBatches);
    }

    /// <summary>Stops accepting events, flushes what is queued (bounded by <see cref="DisposeBound"/>), then stops the pump.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _queue.Writer.TryComplete();
        var finished = await Task.WhenAny(_pump, Task.Delay(DisposeBound)).ConfigureAwait(false);
        if (finished != _pump) await _stop.CancelAsync().ConfigureAwait(false);
        try { await _pump.ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { }
        _stop.Dispose();
    }
}

/// <summary>The seat's context at one assistant turn, for the hub's context bar: the harness estimate of what the
/// next request carries, the model's window and where compaction triggers (0 = never).</summary>
public readonly record struct ContextGauge(int Tokens, int Window, int CompactAt)
{
    public JsonObject ToJson() => new() { ["tokens"] = Tokens, ["window"] = Window, ["compact_at"] = CompactAt };
}
