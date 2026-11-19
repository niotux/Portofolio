using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PiSharp.Core.Agent;
using PiSharp.Core.Notebook;

namespace PiSharp.Tools;

/// <summary>Bounded command lifetimes; never reattach by PID or replay an interrupted mutation.</summary>
public sealed class JobTool : IAgentTool, IAsyncDisposable
{
    public const int DefaultWaitSeconds = 300;
    public const int MaxWaitSeconds = 1800;
    /// <summary>A job's lifetime when start names none.</summary>
    public const int DefaultLifetimeSeconds = 900;
    /// <summary>The longest a job may live: a full test suite on a loaded box (digital-scribe, 2026-10-19: "yes, fix the job
    /// tool limit to 2 hours"; the cap was 1200 s while this description said one hour, and pi10's full App.Tests run was
    /// stopped mid-suite 20 minutes after it started).</summary>
    public const int MaxLifetimeSeconds = 7200;
    /// <summary>Jobs that may run at once by default (Codex keeps many live sessions; 2 until 2026-10-25, plan-pi-sharp-codex
    /// item 5). The seat's memory cap still bounds what they can do together.</summary>
    public const int DefaultMaxActiveJobs = 4;
    /// <summary>Jobs this registry runs at once (setting maxJobs).</summary>
    public int MaxActive { get; init; } = DefaultMaxActiveJobs;

    private sealed class Job(CancellationTokenSource stop)
    {
        private TaskCompletionSource _changed = NewSignal();
        public CancellationTokenSource Stop { get; } = stop;
        public Task Run { get; set; } = Task.CompletedTask;
        public string Output { get; set; } = "";
        public int? ExitCode { get; set; }
        public SessionOutput Buffer { get; init; } = new();
        /// <summary>Pipe to the command's stdin; null unless the job was started with stdin=true.</summary>
        public StreamWriter? Stdin { get; set; }
        public bool WantsStdin { get; init; }
        /// <summary>Completes when the shell has started and handed over its stdin pipe.</summary>
        public TaskCompletionSource StdinAttached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>Completes on the next output update or exit; waiters take a fresh one each round.</summary>
        public Task Changed => Volatile.Read(ref _changed).Task;
        public void Signal() => Interlocked.Exchange(ref _changed, NewSignal()).TrySetResult();
        private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>Where the row came from: a job start, or a bash command promoted at yield.</summary>
        public string Origin { get; set; } = "job";
    }
    private readonly NotebookStore _store;
    private readonly string _cwd;
    private readonly Dictionary<string, Job> _jobs = [];
    public JobTool(string cwd, NotebookStore store)
    {
        _store = store; _cwd = cwd;
        foreach (var row in store.List(kind: "attempt", status: "open").Where(r => r["id"]!.GetValue<string>().StartsWith("job-", StringComparison.Ordinal)))
            store.Put(row["id"]!.GetValue<string>(), "attempt", row["text"] + "\nInterrupted by process restart; outcome unknown. Inspect workspace before retrying.", row["task"]!.GetValue<string>(), "blocked", row["revision"]!.GetValue<long>());
    }
    public string Name => "job";
    public string Label => Name;
    public string Description => "Manage a long command without blocking conversation. start returns a durable job ID after at most one second. status returns bounded recent output; cancel kills the command process group. " +
        $"wait blocks until the job exits, the regex until matches output produced after the wait began, or timeoutSeconds elapses (default {DefaultWaitSeconds}, max {MaxWaitSeconds}); prefer it to repeated status polls. " +
        "input writes text (plus a newline unless newline=false; eof=true then closes stdin) to a job started with stdin=true. " +
        $"Max {DefaultMaxActiveJobs} active jobs (unless the seat sets another number). A job lives {DefaultLifetimeSeconds / 60} minutes unless start names a timeout (max {MaxLifetimeSeconds} s, two hours): give a full test suite a lifetime that covers it, since a job is stopped at its lifetime. " +
        "Interrupted jobs are never automatically replayed. Exit status is evidence, not task completion.";
    public ToolExecutionMode? ExecutionMode => ToolExecutionMode.Sequential;
    public JsonObject Parameters => JsonNode.Parse("""
        {"type":"object","properties":{"op":{"type":"string","enum":["start","status","wait","input","cancel"]},"command":{"type":"string"},"id":{"type":"string"},"task":{"type":"string"},
        "timeout":{"type":"integer","minimum":1,"maximum":7200,"description":"start: job lifetime in seconds (default 900, max 7200 = two hours); the job is stopped when it ends"},
        "stdin":{"type":"boolean","description":"start: keep a pipe to the command's stdin open for op=input (default false: stdin inherited)"},
        "until":{"type":"string","description":"wait: regex; return as soon as new output matches it"},
        "timeoutSeconds":{"type":"integer","minimum":1,"maximum":1800,"description":"wait: give up after this many seconds (default 300)"},
        "text":{"type":"string","description":"input: text to write to stdin"},
        "newline":{"type":"boolean","description":"input: append a newline (default true)"},
        "eof":{"type":"boolean","description":"input: close stdin after writing"},
        "origin":{"type":"string","description":"start: how the command entered the registry; set to 'bash-yield' when the bash tool promoted it"}},"required":["op"]}
        """)!.AsObject();
    public async Task<AgentToolResult> ExecuteAsync(string toolCallId, JsonObject arguments, Action<AgentToolResult>? onUpdate, CancellationToken cancellationToken)
    {
        var op = arguments["op"]!.GetValue<string>();
        var id = arguments["id"]?.GetValue<string>() ?? "";
        string? waited = null;
        if (op == "start")
        {
            var command = arguments["command"]?.GetValue<string>() ?? throw new ArgumentException("command required");
            var task = arguments["task"]?.GetValue<string>() ?? "";
            var timeout = Math.Clamp(arguments["timeout"]?.GetValue<int>() ?? DefaultLifetimeSeconds, 1, MaxLifetimeSeconds);
            var origin = arguments["origin"]?.GetValue<string>() ?? "job";
            var job = new Job(new CancellationTokenSource()) { WantsStdin = arguments["stdin"]?.GetValue<bool>() == true, Origin = origin };
            // One shell per job: the stdin and exit-code hooks belong to that job's process. It has no yield
            // sink, so it never yields: it runs to exit, its lifetime or cancel.
            var bash = new BashTool(_cwd)
            {
                TimeoutCapSeconds = MaxLifetimeSeconds,
                LongWorkHint = $"the job reached its lifetime and was stopped; start it again with a larger timeout, at most {MaxLifetimeSeconds} s",
                StdinSink = job.WantsStdin ? w => { job.Stdin = w; job.StdinAttached.TrySetResult(); } : null,
                ExitCodeSink = code => job.ExitCode = code,
                OutputSession = job.Buffer,
            };
            if (Start(job, command, task, captureBefore: true, jobId =>
                    bash.ExecuteAsync(jobId, new JsonObject { ["command"] = command, ["timeout"] = timeout }, r => { job.Output = Tail(r.Text); job.Signal(); }, job.Stop.Token))
                is not { } started)
                return AgentToolResult.Error(NoFreeSlot);
            id = started;
            await Task.WhenAny(job.Run, Task.Delay(1000, cancellationToken)).ConfigureAwait(false);
        }
        else if (op == "cancel")
        {
            if (_jobs.TryGetValue(id, out var job)) { job.Stop.Cancel(); await job.Run.WaitAsync(cancellationToken).ConfigureAwait(false); }
        }
        else if (op == "wait")
        {
            if (_jobs.TryGetValue(id, out var job))
            {
                var seconds = Math.Clamp(arguments["timeoutSeconds"]?.GetValue<int>() ?? DefaultWaitSeconds, 1, MaxWaitSeconds);
                Regex? until = null;
                if (arguments["until"]?.GetValue<string>() is { Length: > 0 } pattern)
                {
                    try { until = new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.Multiline, TimeSpan.FromSeconds(2)); }
                    catch (ArgumentException e) { return AgentToolResult.Error($"Invalid until regex: {e.Message}"); }
                }
                waited = await WaitAsync(job, until, TimeSpan.FromSeconds(seconds), cancellationToken).ConfigureAwait(false);
            }
            else waited = "not running";
        }
        else if (op == "input")
        {
            if (!_jobs.TryGetValue(id, out var job) || job.Run.IsCompleted) return AgentToolResult.Error("Job is not running; input needs an active job.");
            if (!job.WantsStdin) return AgentToolResult.Error("Job was started without stdin=true; its stdin is not a pipe. Start it again with stdin=true.");
            // start returns after a second even when the shell has not started yet (workspace evidence runs first).
            if (!job.StdinAttached.Task.IsCompleted)
                await Task.WhenAny(job.StdinAttached.Task, job.Run, Task.Delay(TimeSpan.FromSeconds(30), cancellationToken)).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (job.Stdin is not { } stdin) return AgentToolResult.Error("Job stdin is closed.");
            var text = arguments["text"]?.GetValue<string>() ?? "";
            if (arguments["newline"]?.GetValue<bool>() != false) text += "\n";
            try
            {
                await stdin.WriteAsync(text.AsMemory(), cancellationToken).ConfigureAwait(false);
                await stdin.FlushAsync(cancellationToken).ConfigureAwait(false);
                if (arguments["eof"]?.GetValue<bool>() == true) { job.Stdin = null; stdin.Close(); }
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException)
            {
                return AgentToolResult.Error($"Could not write to job stdin (the command may have exited or closed it): {e.Message}");
            }
        }
        else if (op != "status") return AgentToolResult.Error("Unknown job operation.");
        var record = _store.Get(id);
        if (record is null || !id.StartsWith("job-", StringComparison.Ordinal)) return AgentToolResult.Error("Job not found.");
        if (_jobs.TryGetValue(id, out var active))
        {
            if (!active.Run.IsCompleted) record["liveOutput"] = active.Output;
            if (op == "start") record["liveOutput"] = active.Buffer.Read(4000);
            if (active.ExitCode is { } code) record["exitCode"] = code;
        }
        if (waited is not null) record["waited"] = waited;
        return AgentToolResult.FromText(record.ToJsonString());
    }

    private const string NoFreeSlot = "No free job slot: two jobs are already active; finish/cancel one first.";

    /// <summary>
    /// pi-sharp-hh-2: take over a bash command still running at its yield window. The process keeps running
    /// under the job's cap, lifetime (the bash timeout) and evidence rows; it is never killed and re-run.
    /// Returns the bash result for the model, or null when no slot is free (the caller stops the command).
    /// </summary>
    public AgentToolResult? Adopt(BashYield running, string task)
    {
        var buffer = running.Output ?? new SessionOutput();
        var initial = running.Output is null ? running.SoFar : buffer.Read(4000);
        var job = new Job(new CancellationTokenSource()) { Origin = "bash-yield", Output = Tail(initial), WantsStdin = running.Stdin is not null, Buffer = buffer };
        var id = Start(job, running.Command, task, captureBefore: false, _ =>
        {
            // A terminal command's input comes with it (Codex's write_stdin into a running session).
            if (running.Stdin?.Invoke() is { } input) { job.Stdin = input; job.StdinAttached.TrySetResult(); }
            running.Redirect(r => { job.Output = Tail(r.Text); job.Signal(); });
            job.Stop.Token.Register(running.Stop.Cancel);
            return running.Run.ContinueWith(t => { job.ExitCode = running.ExitCode(); return t; }, TaskScheduler.Default).Unwrap();
        });
        return id is null ? null : AgentToolResult.FromText(BashTool.FormatYieldedResult(id, initial));
    }

    /// <summary>Claims a slot and runs <paramref name="execute"/> as the job, recording its evidence row; null when both slots are taken.</summary>
    private string? Start(Job job, string command, string task, bool captureBefore, Func<string, Task<AgentToolResult>> execute)
    {
        lock (_jobs)
        {
            if (_jobs.Values.Count(j => !j.Run.IsCompleted) >= MaxActive) return null;
            var jobId = "job-" + Guid.NewGuid().ToString("N");
            _store.Put(jobId, "attempt", "Running command:\n" + command, task, "open", 0);
            _jobs.Add(jobId, job);
            job.Run = Task.Run(async () =>
            {
                // An adopted command was already running, so a snapshot taken now is not a "before".
                var before = captureBefore
                    ? await WorkspaceEvidence.CaptureAsync(_cwd, job.Stop.Token).ConfigureAwait(false)
                    : new JsonObject { ["available"] = false, ["reason"] = "command was already running when bash promoted it" };
                AgentToolResult result;
                try { result = await execute(jobId).ConfigureAwait(false); }
                catch (Exception ex) { result = AgentToolResult.Error(ex.Message); }
                job.Stdin = null;
                job.Output = Tail(result.Text);
                job.Signal();
                var after = await WorkspaceEvidence.CaptureAsync(_cwd, job.Stop.Token).ConfigureAwait(false);
                var row = _store.Get(jobId)!;
                var evidence = new JsonObject { ["command"] = command, ["output"] = job.Output, ["isError"] = result.IsError, ["exitCode"] = job.ExitCode,
                    ["before"] = before, ["after"] = after, ["completedAt"] = DateTimeOffset.UtcNow.ToString("O"),
                    ["sourceUnchanged"] = before["available"]?.GetValue<bool>() == true && after["available"]?.GetValue<bool>() == true && before["sha256"]!.GetValue<string>() == after["sha256"]!.GetValue<string>(),
                    ["origin"] = job.Origin };
                _store.Put(jobId, "attempt", evidence.ToJsonString(), task,
                    result.IsError ? "blocked" : "completed", row["revision"]!.GetValue<long>());
            });
            return jobId;
        }
    }

    /// <summary>Why the wait ended: "exited", "matched" or "timeout". Throws when the turn is cancelled.</summary>
    /// <summary><see cref="WriteStdinTool"/>: types <paramref name="chars"/> into a running job (as given: no newline added), waits up
    /// to <paramref name="yieldFor"/> or its exit, and returns what it printed since, cut to <paramref name="maxTokens"/>.</summary>
    internal async Task<AgentToolResult> WriteStdinAsync(string id, string chars, TimeSpan yieldFor, int maxTokens, CancellationToken cancellationToken)
    {
        if (!_jobs.TryGetValue(id, out var job)) return AgentToolResult.Error($"No session '{id}': write_stdin takes the job_id a yielded bash command returned.");
        if (chars.Length > 0)
        {
            if (job.Run.IsCompleted) return AgentToolResult.Error($"Session {id} has ended (exit {job.ExitCode?.ToString() ?? "?"}); nothing can be typed into it. Its last output:\n{OutputCap.MiddleCut(job.Output, maxTokens)}");
            if (!job.WantsStdin) return AgentToolResult.Error($"Session {id} takes no input: run the command with bash tty=true (or job start stdin=true) to type into it.");
            if (!job.StdinAttached.Task.IsCompleted)
                await Task.WhenAny(job.StdinAttached.Task, job.Run, Task.Delay(TimeSpan.FromSeconds(30), cancellationToken)).ConfigureAwait(false);
            if (job.Stdin is not { } stdin) return AgentToolResult.Error($"Session {id}'s input is closed.");
            try
            {
                await stdin.WriteAsync(chars.AsMemory(), cancellationToken).ConfigureAwait(false);
                await stdin.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException)
            {
                return AgentToolResult.Error($"Could not type into session {id} (the command may have exited or closed its input): {e.Message}");
            }
        }
        if (yieldFor > TimeSpan.Zero) await WaitAsync(job, null, yieldFor, cancellationToken).ConfigureAwait(false);
        var fresh = job.Buffer.Read(Math.Max(2, maxTokens * OutputCap.BytesPerToken / 3));
        var running = !job.Run.IsCompleted;
        var result = new JsonObject
        {
            ["session_id"] = id,
            ["output"] = fresh,
            ["more_output"] = job.Buffer.HasUnread,
            ["running"] = running,
        };
        if (!running && job.ExitCode is { } code) result["exit_code"] = code;
        return AgentToolResult.FromText(result.ToJsonString());
    }

    private static async Task<string> WaitAsync(Job job, Regex? until, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var baseline = job.Output;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        var expired = Task.Delay(Timeout.Infinite, deadline.Token);
        while (true)
        {
            // Take the signal before reading state, so an update between the two is not missed.
            var changed = job.Changed;
            if (until is not null && until.IsMatch(NewOutput(baseline, job.Output))) return "matched";
            if (job.Run.IsCompleted) return "exited";
            await Task.WhenAny(changed, job.Run, expired).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (expired.IsCompleted) return until is not null && until.IsMatch(NewOutput(baseline, job.Output)) ? "matched" : job.Run.IsCompleted ? "exited" : "timeout";
        }
    }

    /// <summary>
    /// The part of <paramref name="current"/> produced after <paramref name="baseline"/>. Both are tails of a
    /// sliding window, so the current tail starts with the longest suffix of the baseline still in view; if none
    /// is, everything is new.
    /// </summary>
    internal static string NewOutput(string baseline, string current)
    {
        for (var k = 0; k < baseline.Length; k++)
        {
            var kept = baseline.AsSpan(k);
            if (current.AsSpan().StartsWith(kept, StringComparison.Ordinal)) return current[kept.Length..];
        }
        return current;
    }

    internal static string Tail(string text)
    {
        const int limit = 8000;
        const int markerReserve = 100;
        if (text.Length <= limit) return text;
        var contentLimit = limit - markerReserve;
        var head = contentLimit / 5;
        var tail = contentLimit - head;
        var omittedText = text.Substring(head, text.Length - head - tail);
        var omittedLines = Truncation.SplitLinesForCounting(omittedText).Count;
        var omittedBytes = System.Text.Encoding.UTF8.GetByteCount(text) -
            System.Text.Encoding.UTF8.GetByteCount(text.AsSpan(0, head)) -
            System.Text.Encoding.UTF8.GetByteCount(text.AsSpan(text.Length - tail));
        return text[..head] + $"\n[... {omittedLines} lines / {omittedBytes} bytes omitted]\n" + text[^tail..];
    }
    public async ValueTask DisposeAsync()
    {
        foreach (var job in _jobs.Values) job.Stop.Cancel();
        try { await Task.WhenAll(_jobs.Values.Select(j => j.Run)).ConfigureAwait(false); }
        finally { foreach (var job in _jobs.Values) job.Stop.Dispose(); }
    }
}
