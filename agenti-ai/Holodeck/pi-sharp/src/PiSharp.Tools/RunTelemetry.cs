// plan-pi-sharp-codex item 20: one run's events for the seat's telemetry: each model request (duration, tokens, how it
// ended), each tool call (duration, error or not), each guard decision, and the run's turn diff (item 19) at its end.
using System.Diagnostics;
using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Factory;
using PiSharp.Core.Guard;
using PiSharp.Core.Messages;

namespace PiSharp.Tools;

public sealed class RunTelemetry(SeatTelemetry? sink, TurnDiffTracker diffs, Func<DateTime>? clock = null)
{
    private readonly Func<DateTime> _now = clock ?? (() => DateTime.UtcNow);
    private readonly Dictionary<string, long> _toolStarts = new(StringComparer.Ordinal);
    private long _requestStart;

    public TurnDiffTracker Diffs => diffs;

    /// <summary>Times requests and tool calls from the run's events.</summary>
    public void Observe(AgentEvent evt)
    {
        if (sink is null) return;
        switch (evt)
        {
            case MessageStart { Message: AssistantMessage }:
                _requestStart = Stopwatch.GetTimestamp();
                break;
            case MessageEnd { Message: AssistantMessage message }:
                sink.Record(Request(message, _requestStart == 0 ? null : Elapsed(_requestStart)));
                _requestStart = 0;
                break;
            case ToolExecutionStart start:
                _toolStarts[start.ToolCallId] = Stopwatch.GetTimestamp();
                break;
            case ToolExecutionEnd end:
                int? ms = _toolStarts.Remove(end.ToolCallId, out var began) ? Elapsed(began) : null;
                sink.Record(new SeatEvent(_now(), SeatEvent.Tool, end.ToolName, !end.IsError, ms,
                    Detail: end.IsError ? new JsonObject { ["error"] = Cut(ContentText.Of(end.Result.Content), 300) } : null));
                break;
        }
    }

    public SeatEvent Request(AssistantMessage message, int? durationMs) =>
        new(_now(), SeatEvent.Request, message.ResponseModel ?? message.Model, message.StopReason is not (StopReason.Error or StopReason.Aborted),
            durationMs, message.Usage.Input, message.Usage.Output, message.Usage.CacheRead, new JsonObject
            {
                ["stop"] = message.StopReason.ToString().ToLowerInvariant(),
                ["provider"] = message.Provider,
                ["error_code"] = message.ErrorCode,
                ["tool_calls"] = message.ToolCalls.Count(),
            });

    /// <summary>The guard's decision on a call (<see cref="AgentLoopConfig.RecordGuard"/>).</summary>
    public void Guard(ToolCallContent call, GuardVerdict verdict, string decision) =>
        sink?.Record(new SeatEvent(_now(), SeatEvent.Guard, verdict.Rule.Id, decision == "allowed", Detail: new JsonObject
        {
            ["tool"] = call.Name,
            ["action"] = verdict.Action.ToString().ToLowerInvariant(),
            ["decision"] = decision,
            ["call"] = Cut(call.Arguments.ToJsonString(), 300),
        }));

    /// <summary>The run's turn diff: logged in a line and sent; null when no file the file tools touched changed.</summary>
    public TurnDiffResult? Finish(Action<string> log)
    {
        var result = diffs.Finish();
        if (result is null) return null;
        log("[turn] changed " + result.Line());
        sink?.Record(new SeatEvent(_now(), SeatEvent.TurnDiff, null, true, Detail: result.ToJson()));
        return result;
    }

    private static int Elapsed(long start) => (int)Math.Min(int.MaxValue, Stopwatch.GetElapsedTime(start).TotalMilliseconds);

    private static string Cut(string text, int max) => text.Length <= max ? text : text[..max] + "...";
}
