// Ported from pi: packages/coding-agent/src/core/tools/bash.ts @ 71dca871b
// Linux: /bin/bash -c, process-group kill. Windows: Git Bash when present (dev-host tests).
// macOS paths are not ported (decision 13).
using System.Text;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using PiSharp.Core.Agent;

namespace PiSharp.Tools;

/// <summary>Execute a bash command, merging stdout and stderr, with timeout and abort killing the process tree.</summary>
public sealed class BashTool : BuiltinTool<BashArgs>
{
    public const int UpdateThrottleMs = 100;
    public const int MaxTimeoutMs = int.MaxValue;
    public static readonly double MaxTimeoutSeconds = MaxTimeoutMs / 1000.0;
    /// <summary>Applied when the call names no timeout: a command that never returns must not freeze the seat.</summary>
    public const double DefaultTimeoutSeconds = 600;
    /// <summary>Hard cap on the requested timeout, enforced by clamping and reported in the result.</summary>
    public const double MaxAllowedTimeoutSeconds = 3600;
    /// <summary>The longest timeout this shell accepts: <see cref="MaxAllowedTimeoutSeconds"/> for a turn's own bash; a job's
    /// shell takes the job's lifetime instead (<see cref="JobTool.MaxLifetimeSeconds"/>).</summary>
    public double TimeoutCapSeconds { get; init; } = MaxAllowedTimeoutSeconds;
    /// <summary>What a timed-out command is told to do next.</summary>
    public string LongWorkHint { get; init; } = "pass a larger timeout or use the job tool for long work";
    /// <summary>pi-sharp-hh-2: how long a bash call may block the turn before it yields to a job.</summary>
    public const double DefaultYieldSeconds = 30;
    /// <summary>pi-sharp-hh-2: a yield window smaller than 1 s defeats its purpose; 60 s caps the blocking window.</summary>
    public const double MinYieldSeconds = 1, MaxYieldSeconds = 60;

    /// <summary>What the commands see of the seat's environment (set once at start; every command and job uses it).</summary>
    public static ShellEnvironmentPolicy EnvironmentPolicy { get; set; } = ShellEnvironmentPolicy.None;

    /// <summary>What every command gets (Codex's exec environment, plan-pi-sharp-codex item 6): no colour, no pager, a plain
    /// terminal, and git never waiting on a password prompt, so output is short and nothing waits for a person. The locale is
    /// left as the seat has it. <see cref="EnvironmentPolicy"/>'s set comes after and may override these.</summary>
    public static readonly IReadOnlyDictionary<string, string> FixedEnvironment = new Dictionary<string, string>
    {
        ["NO_COLOR"] = "1", ["TERM"] = "dumb", ["COLORTERM"] = "", ["PAGER"] = "cat", ["GIT_PAGER"] = "cat", ["GH_PAGER"] = "cat",
        ["GIT_TERMINAL_PROMPT"] = "0",
        // UTF-8 whatever the seat's own locale (Codex's unified exec sets the same three).
        ["LANG"] = "C.UTF-8", ["LC_CTYPE"] = "C.UTF-8", ["LC_ALL"] = "C.UTF-8",
    };

    /// <summary>util-linux script, which runs a command in a pseudo-terminal.</summary>
    public static string ScriptPath { get; set; } = "/usr/bin/script";

    private static readonly string[] SessionEnvVars =
    [
        "PI_SESSION_ID", "PI_SESSION_FILE", "PI_PROVIDER", "PI_MODEL", "PI_REASONING_LEVEL",
    ];

    private static readonly JsonObject ArgsSchema = Schema.Parse("""
        {
          "type": "object",
          "properties": {
            "command": { "type": "string", "description": "Shell command to execute" },
            "timeout": { "type": "number", "description": "Timeout in seconds (optional, default 600; values above 3600 are clamped)" },
            "yield": { "type": "number", "description": "Seconds to run in the foreground before yielding to a job (optional, default 30, clamped to 1-60; only while a job registry is attached)" },
            "tty": { "type": "boolean", "description": "Run the command in a terminal (PTY), for a program that needs one (an interactive prompt, a progress display); default false. A tty command still running at the yield keeps its input open: type into it with write_stdin" },
            "max_output_tokens": { "type": "number", "description": "Cap on the output returned, in tokens (optional): the head and tail are kept, the middle is cut, and the original size is said" }
          },
          "required": ["command"]
        }
        """);

    private static readonly string Describe =
        $"Execute a bash command in the current working directory. Returns stdout and stderr. " +
        $"Output is truncated to last {Truncation.DefaultMaxLines} lines or {Truncation.DefaultMaxBytes / 1024}KB " +
        $"(whichever is hit first). If truncated, full output is saved to a temp file. " +
        $"Optionally provide a timeout in seconds (default {DefaultTimeoutSeconds:0}, max {MaxAllowedTimeoutSeconds:0}) and a yield in seconds " +
        $"(default {DefaultYieldSeconds:0}, clamped to {MinYieldSeconds:0}-{MaxYieldSeconds:0}): a command still running at the yield becomes a " +
        $"job tool job (same registry, job cap, lifetime and evidence rows) and the result carries the output so far " +
        $"plus job_id and 'still running; use job wait/input/kill to follow it up'.";

    public BashTool(string cwd)
        : base("bash", "bash", Describe, ArgsSchema, ToolsJsonContext.Default.BashArgs, cwd)
    {
    }

    /// <summary>
    /// Job tool hook: when set, stdin is a pipe handed to this callback right after start, instead of the
    /// inherited stdin. The writer is disposed with the process.
    /// </summary>
    internal Action<StreamWriter>? StdinSink { get; init; }
    internal SessionOutput? OutputSession { get; init; }

    /// <summary>Job tool hook: receives the shell's exit code as soon as it exits (before output is drained).</summary>
    internal Action<int>? ExitCodeSink { get; init; }

    /// <summary>
    /// pi-sharp-checkpoints-3: receives the command text and the shell's exit code as soon as the
    /// process exits (same moment as <see cref="ExitCodeSink"/>, before the non-zero exit becomes an
    /// error). Wired by the built-in tool set so the checkpoint green tracker can tell whether a
    /// configured build just passed.
    /// </summary>
    internal Action<string, int>? CommandExitSink { get; init; }

    /// <summary>
    /// pi-sharp-hh-2: hand a command that is still running at the yield window to the job registry
    /// (<see cref="JobTool.Adopt"/>) instead of blocking the turn. Returns the AgentToolResult to return to
    /// the model, or null when the registry has no free slot (bash then stops the command). Yield applies
    /// only while this is set, so a job's own shell never yields.
    /// </summary>
    public Func<BashYield, Task<AgentToolResult?>>? YieldSink { get; set; }

    /// <summary>
    /// A command this seat must not run through the shell, refused before it starts: returns why, or null to run it.
    /// The desktop toolkit sets it so a screen grab goes through desktop__capture, which publishes it as evidence
    /// (adr-visual-toolkits section 6).
    /// </summary>
    public Func<string, string?>? CommandRefusal { get; set; }

    protected override async Task<AgentToolResult> ExecuteAsync(
        string toolCallId, BashArgs args, Action<AgentToolResult>? onUpdate, CancellationToken cancellationToken)
    {
        if (args.MaxOutputTokens is not { } cap || cap <= 0) return await ExecuteCoreAsync(args, onUpdate, cancellationToken).ConfigureAwait(false);
        // Codex's max_output_tokens: every way the command ends (a non-zero exit too) comes back cut to the cap.
        var tokens = (int)Math.Min(cap, 1_000_000);
        try { return Capped(await ExecuteCoreAsync(args, onUpdate, cancellationToken).ConfigureAwait(false), tokens); }
        catch (InvalidOperationException e) { throw new InvalidOperationException(OutputCap.MiddleCut(e.Message, tokens), e); }
    }

    private async Task<AgentToolResult> ExecuteCoreAsync(BashArgs args, Action<AgentToolResult>? onUpdate, CancellationToken cancellationToken)
    {
        if (CommandRefusal?.Invoke(args.Command) is { } refused) return AgentToolResult.Error(refused);
        // pi-sharp-hh-2: a named timeout shorter than the yield window is honored as-is (timed-out, not
        // promoted); the default 600 s leaves headroom to a real yield.
        double? yieldSeconds = YieldSink is null || (args.Timeout is not null && args.Timeout < DefaultYieldSeconds) ? null
            : Math.Clamp(args.Yield ?? DefaultYieldSeconds, MinYieldSeconds, MaxYieldSeconds);
        if (yieldSeconds is not { } ys) return await RunAsync(args, onUpdate, null, cancellationToken).ConfigureAwait(false);

        // The command runs under its own stop token, tied to this turn only until a job adopts it. Promotion
        // hands over the running process; it never kills and re-runs the command.
        var stop = new CancellationTokenSource();
        var turn = cancellationToken.Register(stop.Cancel);
        Action<AgentToolResult>? target = onUpdate;
        var soFar = "";
        int? exitCode = null;
        // A terminal command keeps its input open (Codex's write_stdin): if it yields, the job takes the pipe, so the model
        // can type into it. Only here, where it can become a job: a command that never yields keeps the seat's own stdin.
        StreamWriter? ttyInput = null;
        var sessionOutput = new SessionOutput();
        var run = RunAsync(args, r =>
        {
            Volatile.Write(ref soFar, r.Text);
            Volatile.Read(ref target)?.Invoke(r);
        }, code => exitCode = code, stop.Token, args.Tty == true ? w => Volatile.Write(ref ttyInput, w) : null, sessionOutput);

        var handedOver = false;
        try
        {
            try { await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(ys), cancellationToken)).ConfigureAwait(false); }
            catch (OperationCanceledException) { /* the turn's cancellation stops the command via the registration */ }
            if (run.IsCompleted || cancellationToken.IsCancellationRequested) return await run.ConfigureAwait(false);

            // From here this tool call is ending: its update callback must not see the job's later output.
            Volatile.Write(ref target, null);
            turn.Dispose();
            var promoted = await YieldSink!(new BashYield(args.Command, Volatile.Read(ref soFar), run, () => exitCode, stop,
                redirect => Volatile.Write(ref target, redirect), args.Tty == true ? () => Volatile.Read(ref ttyInput) : null, sessionOutput)).ConfigureAwait(false);
            if (promoted is not null) { handedOver = true; return promoted; }

            stop.Cancel();
            try { await run.ConfigureAwait(false); } catch (InvalidOperationException) { /* stopped: reported below */ }
            var secs = (args.Timeout ?? DefaultTimeoutSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
            throw new InvalidOperationException(AppendStatus(Volatile.Read(ref soFar),
                $"Command still running at the {ys:0} s yield; no free job slot, so it was stopped. Pass a larger timeout or use the job tool for long work; it would have timed out after {secs} seconds"));
        }
        finally
        {
            turn.Dispose();
            if (!handedOver) _ = run.ContinueWith(_ => stop.Dispose(), TaskScheduler.Default);
        }
    }

    /// <summary>The result's text cut to the call's max_output_tokens (Codex's cap).</summary>
    private static AgentToolResult Capped(AgentToolResult result, int tokens) =>
        result with { Content = [.. result.Content.Select(c => c is Core.Messages.TextContent t ? new Core.Messages.TextContent(OutputCap.MiddleCut(t.Text, tokens)) : c)] };

    /// <summary>Runs the command to completion, timeout or <paramref name="cancellationToken"/>; never yields.</summary>
    private async Task<AgentToolResult> RunAsync(
        BashArgs args, Action<AgentToolResult>? onUpdate, Action<int>? exitCodeSink, CancellationToken cancellationToken,
        Action<StreamWriter>? ttyInput = null, SessionOutput? sessionOutput = null)
    {
        var timeoutSeconds = args.Timeout ?? DefaultTimeoutSeconds;
        var timeoutClamped = 0.0;
        if (timeoutSeconds > TimeoutCapSeconds)
        {
            timeoutClamped = timeoutSeconds;
            timeoutSeconds = TimeoutCapSeconds;
        }
        var timeoutMs = ResolveTimeoutMs(timeoutSeconds);
        if (!Directory.Exists(Cwd))
        {
            throw new InvalidOperationException($"Working directory does not exist: {Cwd}\nCannot execute bash commands.");
        }

        var shell = ResolveShell();
        // tty (Codex exec_command's tty, plan-pi-sharp-codex item 5): the command runs under util-linux script, which gives it
        // a terminal; without script it runs as before and says so.
        var command = args.Command;
        var ttyNote = "";
        if (args.Tty == true)
        {
            if (OperatingSystem.IsLinux() && File.Exists(ScriptPath))
                command = $"exec {ScriptPath} -qefc '{args.Command.Replace("'", "'\\''")}' /dev/null";
            else { ttyNote = "(no terminal available here: ran without one)\n"; ttyInput = null; }
        }
        var stdinSink = StdinSink ?? ttyInput;
        using var output = new OutputAccumulator(tempFilePrefix: "pi-bash");
        var outputLock = new object();
        var acceptingOutput = true;
        var lastUpdateAt = 0L;
        var updateDirty = false;
        Timer? updateTimer = null;

        void EmitOutputUpdate()
        {
            if (onUpdate is null) return;
            // The pump and the throttle timer both emit; one lock around snapshot and delivery keeps an older
            // snapshot from landing after a newer one (a job's wait would then never see the last output).
            lock (outputLock)
            {
                if (!updateDirty) return;
                updateDirty = false;
                lastUpdateAt = Environment.TickCount64;
                var snapshot = output.Snapshot(persistIfTruncated: true);
                onUpdate(new AgentToolResult(
                    [new Core.Messages.TextContent(snapshot.Content)],
                    DetailsFrom(snapshot)));
            }
        }

        void ScheduleOutputUpdate()
        {
            if (onUpdate is null) return;
            updateDirty = true;
            long sinceLast = Environment.TickCount64 - lastUpdateAt;
            int delay = (int)(sinceLast < 0 || sinceLast >= UpdateThrottleMs ? 0 : UpdateThrottleMs - sinceLast);
            if (delay <= 0)
            {
                updateTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                EmitOutputUpdate();
                return;
            }
            updateTimer ??= new Timer(_ =>
            {
                try { EmitOutputUpdate(); }
                catch (ObjectDisposedException) { /* tool finishing */ }
            }, null, Timeout.Infinite, Timeout.Infinite);
            updateTimer.Change(delay, Timeout.Infinite);
        }

        onUpdate?.Invoke(new AgentToolResult([], null));

        var psi = new ProcessStartInfo
        {
            FileName = shell.FileName,
            WorkingDirectory = Cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = shell.CommandFromStdin || stdinSink is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (OperatingSystem.IsLinux() && File.Exists("/usr/bin/setsid"))
        {
            psi.FileName = "/usr/bin/setsid";
            psi.ArgumentList.Add(shell.FileName);
        }
        foreach (var arg in shell.Args) psi.ArgumentList.Add(arg);
        if (!shell.CommandFromStdin) psi.ArgumentList.Add(command);
        foreach (var key in SessionEnvVars) psi.Environment.Remove(key);
        foreach (var (name, value) in FixedEnvironment) psi.Environment[name] = value;
        EnvironmentPolicy.Apply(psi.Environment);

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        try
        {
            if (!process.Start()) throw new InvalidOperationException("Failed to start bash");
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new InvalidOperationException($"Failed to start bash: {e.Message}");
        }

        if (shell.CommandFromStdin)
        {
            await process.StandardInput.WriteAsync(command.AsMemory(), cancellationToken).ConfigureAwait(false);
            process.StandardInput.Close();
        }
        else if (stdinSink is not null)
        {
            process.StandardInput.AutoFlush = true;
            stdinSink(process.StandardInput);
        }

        if (OperatingSystem.IsFreeBSD() || (OperatingSystem.IsLinux() && psi.FileName != "/usr/bin/setsid"))
        {
            try { SetPgid(process.Id, process.Id); } catch { /* already in its own group, or raced with exec */ }
        }

        using var timeoutCts = new CancellationTokenSource(timeoutMs);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var timedOut = false;
        var aborted = false;

        void OnCancel()
        {
            if (cancellationToken.IsCancellationRequested) aborted = true;
            else timedOut = true;
            KillProcessTree(process);
        }

        await using var reg = linked.Token.Register(OnCancel);

        async Task PumpAsync(Stream stream)
        {
            var buffer = new byte[8192];
            var decoder = System.Text.Encoding.UTF8.GetDecoder();
            var chars = new char[System.Text.Encoding.UTF8.GetMaxCharCount(8192)];
            while (true)
            {
                int n;
                try
                {
                    n = await stream.ReadAsync(buffer, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception e) when (e is IOException or ObjectDisposedException)
                {
                    break;
                }
                if (n <= 0) break;
                if (!acceptingOutput) continue;
                var count = decoder.GetChars(buffer.AsSpan(0, n), chars, flush: false);
                var decoded = new string(chars, 0, count);
                (sessionOutput ?? OutputSession)?.Append(decoded);
                try { lock (outputLock) output.Append(buffer.AsSpan(0, n), decoded); }
                catch { KillProcessTree(process); throw; }
                ScheduleOutputUpdate();
            }
            var final = decoder.GetChars([], chars, flush: true);
            if (final > 0)
            {
                var decoded = new string(chars, 0, final);
                (sessionOutput ?? OutputSession)?.Append(decoded);
                lock (outputLock) output.Append([], decoded);
            }
        }

        var stdoutTask = PumpAsync(process.StandardOutput.BaseStream);
        var stderrTask = PumpAsync(process.StandardError.BaseStream);

        try
        {
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            ExitCodeSink?.Invoke(process.ExitCode);
            exitCodeSink?.Invoke(process.ExitCode);
            CommandExitSink?.Invoke(args.Command, process.ExitCode);
        }
        catch (Exception)
        {
            KillProcessTree(process);
            throw;
        }

        // Descendants can keep redirected pipes open after bash exits. Cancellation still kills the
        // group; a completed foreground command must not hang forever draining inherited handles.
        try { await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
        catch (TimeoutException)
        {
            KillProcessTree(process);
            // The kill above reaches the descendants (owned tree / pipe holders). If it worked, the
            // write ends close and the drain finishes on EOF: the command's own result stands. Only a
            // drain that still cannot finish means the output is genuinely retained.
            try { await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
            catch (TimeoutException)
            {
                process.StandardOutput.Close(); process.StandardError.Close();
                await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
                throw new InvalidOperationException("Command exited but descendants retained output pipes; process group stopped.");
            }
        }

        OutputSnapshot Finish()
        {
            acceptingOutput = false;
            lock (outputLock)
            {
                output.Finish();
                updateTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                EmitOutputUpdate();
                var snapshot = output.Snapshot(persistIfTruncated: true);
                output.CloseTempFile();
                return snapshot;
            }
        }

        var snapshot = Finish();
        updateTimer?.Dispose();

        var (text, details) = FormatOutput(snapshot, output.LastLineBytes, emptyText: aborted || timedOut ? "" : "(no output)");
        if (args.Tty == true) text = ttyNote + text.Replace("\r\n", "\n");

        if (aborted) throw new InvalidOperationException(AppendStatus(text, "Command aborted"));
        if (timedOut)
        {
            var secs = timeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            // pi-sharp-hh-2: the work item requires the result to say when a >3600 s timeout was clamped.
            var clampedNote = timeoutClamped > 0
                ? $" (the requested {timeoutClamped:0} s was clamped to {TimeoutCapSeconds:0} s)" : "";
            var hint = args.Timeout is null ? $" (the default; {LongWorkHint})" : clampedNote + $" ({LongWorkHint})";
            throw new InvalidOperationException(AppendStatus(text, $"Command timed out after {secs} seconds{hint}"));
        }
        if (process.ExitCode != 0)
        {
            var failed = AppendStatus(text, $"Command exited with code {process.ExitCode}");
            if (NameHint is not null && FailedName(text) is { } name
                && await HintAsync(name, cancellationToken).ConfigureAwait(false) is { Length: > 0 } hint)
                failed = AppendStatus(failed, $"KB names: {hint}");
            throw new InvalidOperationException(failed);
        }

        return new AgentToolResult([new Core.Messages.TextContent(text)], details);
    }

    private static int ResolveTimeoutMs(double timeout)
    {
        if (double.IsNaN(timeout) || double.IsInfinity(timeout) || timeout <= 0)
        {
            throw new InvalidOperationException("Invalid timeout: must be a finite number of seconds");
        }
        var timeoutMs = timeout * 1000;
        if (timeoutMs > MaxTimeoutMs)
        {
            throw new InvalidOperationException($"Invalid timeout: maximum is {MaxTimeoutSeconds} seconds");
        }
        return (int)timeoutMs;
    }

    /// <summary>
    /// pi-sharp-hh-2: builds the bash yield result. The job id and the 'still running' hint are required by the
    /// work item's acceptance; the text so far rides along so the model can reason about it.
    /// </summary>
    public static string FormatYieldedResult(string jobId, string soFar)
    {
        var note = "still running; use job wait/input/kill to follow it up";
        return string.IsNullOrEmpty(soFar) ? $"job_id={jobId}\n{note}" : soFar + $"\n\njob_id={jobId}\n{note}";
    }

    private static (string Text, JsonObject? Details) FormatOutput(OutputSnapshot snapshot, int lastLineBytes, string emptyText)
    {
        var truncation = snapshot.Truncation;
        var text = string.IsNullOrEmpty(snapshot.Content) ? emptyText : snapshot.Content;
        JsonObject? details = null;
        if (truncation.Truncated)
        {
            details = DetailsFrom(snapshot);
            var omittedLines = Math.Max(0, truncation.TotalLines - truncation.OutputLines);
            var omittedBytes = Math.Max(0, truncation.TotalBytes - truncation.OutputBytes);
            text = text.Replace("[... omitted output ...]", $"[... {omittedLines} lines / {omittedBytes} bytes omitted; full output: {snapshot.FullOutputPath}]");
        }
        return (text, details);
    }

    private static JsonObject? DetailsFrom(OutputSnapshot snapshot)
    {
        if (!snapshot.Truncation.Truncated) return null;
        var details = new JsonObject { ["truncation"] = snapshot.Truncation.ToJson() };
        if (snapshot.FullOutputPath is not null) details["fullOutputPath"] = snapshot.FullOutputPath;
        return details;
    }

    /// <summary>
    /// Factory hook: the KB's "did you mean" for a URL or host (kb_resolve). Asked only when a command failed on a
    /// name it could not reach - never on success, never on any other failure - and the answer, if any, is appended
    /// to the error the model reads. Null (not attached) or a KB that does not answer changes nothing.
    /// </summary>
    public Func<string, CancellationToken, Task<string?>>? NameHint { get; set; }

    internal static readonly TimeSpan NameHintTimeout = TimeSpan.FromSeconds(5);

    // Most specific first: a failed URL names the repository, a failed host only the machine.
    private static readonly System.Text.RegularExpressions.Regex[] FailedNamePatterns =
    [
        new(@"repository '([^'\s]+)' not found"),
        new(@"unable to access '([^'\s]+)'"),
        new(@"'([^'\s]+)' does not appear to be a git repository"),
        new(@"Could not resolve host(?:name)?:? ([^\s:'""]+)"),
    ];

    /// <summary>The URL or host a failed command could not reach, from git/curl/ssh's own wording; null otherwise.</summary>
    internal static string? FailedName(string output)
    {
        foreach (var pattern in FailedNamePatterns)
            if (pattern.Match(output) is { Success: true } m)
                return m.Groups[1].Value.TrimEnd('.', '/');
        return null;
    }

    private async Task<string?> HintAsync(string name, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(NameHintTimeout);
        try { return await NameHint!(name, deadline.Token).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
    }

    private static string AppendStatus(string text, string status) =>
        string.IsNullOrEmpty(text) ? status : $"{text}\n\n{status}";

    internal readonly record struct ShellConfig(string FileName, IReadOnlyList<string> Args, bool CommandFromStdin);

    internal static ShellConfig ResolveShell()
    {
        if (File.Exists("/bin/bash")) return new("/bin/bash", ["-c"], false);

        if (OperatingSystem.IsWindows())
        {
            var candidates = new List<string>();
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrEmpty(pf)) candidates.Add(Path.Combine(pf, "Git", "bin", "bash.exe"));
            var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrEmpty(pf86)) candidates.Add(Path.Combine(pf86, "Git", "bin", "bash.exe"));
            foreach (var path in candidates)
            {
                if (File.Exists(path)) return new(path, ["-c"], false);
            }

            var onPath = FindOnPath("bash.exe") ?? FindOnPath("bash");
            if (onPath is not null) return new(onPath, ["-c"], false);
        }
        else
        {
            var onPath = FindOnPath("bash");
            if (onPath is not null) return new(onPath, ["-c"], false);
            if (File.Exists("/bin/sh")) return new("/bin/sh", ["-c"], false);
        }

        throw new InvalidOperationException("No bash shell found.");
    }

    private static string? FindOnPath(string executable)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, executable);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    internal static void KillProcessTree(Process process)
    {
        try
        {
            if (OperatingSystem.IsWindows() && process.HasExited) return;
        }
        catch { return; }

        var pid = process.Id;
        if (OperatingSystem.IsWindows())
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            return;
        }

        if (OperatingSystem.IsLinux() && OwnedTreeKill(process, pid)) return;

        // Legacy: stop the shell's own process group (drain path after a normal exit, or a broken /proc).
        try { Kill(-pid, 9); } catch { /* not a process group leader */ }
        try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
    }

    // pi-sharp-job-cancel-nested-process-groups: stop the tree we own, not just our own group. A command's
    // children can sit in nested process groups or even new sessions (GNU timeout's watchdog setsids when
    // it is not tty-foreground), so "kill our group" leaves them holding the output pipes. Parent links
    // survive re-sessioning, so we enumerate /proc from the root pid: if the root has already exited (the
    // setsid wrapper does, before the shell execs), it is re-parented to its live child, then two BFS
    // passes (union) catch in-flight forks; we SIGKILL each distinct process group we own, then each pid
    // with its start-time re-verified, so a recycled pid can never be signalled.
    private static bool OwnedTreeKill(Process process, int rootPid)
    {
        var pgroups = new HashSet<int>();
        var roots = new Queue<int>();
        var owned = new Dictionary<int, long>();
        for (int depth = 0; depth < 3; depth++)
        {
            (int pid, int ppid, long starttime, int pgrp)? root = null;
            try { root = ReadStartAndPgrp(rootPid); } catch { /* gone */ }
            if (root is { } r)
            {
                pgroups.Add(r.pgrp);
                roots.Enqueue(rootPid);
                break;
            }
            // The wrapper exited before the command started: its live child carries on (ppid reparenting
            // is synchronous, so exactly one child is our tree).
            var child = FirstChild(rootPid);
            if (child is not { } c)
            {
                // pi-sharp-job-cancel-nested-process-groups (return): the root exited and no live
                // child carries on (the drain path after a normal exit): the descendants were
                // reparented to init and are invisible to the ppid walk. The private output pipes
                // are the remaining ownership proof: only a descendant of this command holds
                // their write ends. Collect the holders, then fall through to the kill loop with
                // them; if no pipe target resolves, keep the legacy behavior.
                if (!PipeHolders(process, owned, pgroups)) return false;
                break;
            }
            rootPid = c.pid;
        }
        // roots is empty only on the pipe-holder path above, where the BFS below is a no-op and the
        // kill loop signals the collected holders; with neither roots nor holders there is nothing owned.
        if (roots.Count == 0 && owned.Count == 0) return false;

        for (int pass = 0; pass < 2; pass++)
        {
            var byPpid = new Dictionary<int, List<(int pid, long starttime, int pgrp)>>();
            foreach (var entry in Directory.EnumerateDirectories("/proc"))
            {
                var name = Path.GetFileName(entry);
                if (!int.TryParse(name, out var pid) || pid == rootPid) continue;
                (int pid, int ppid, long starttime, int pgrp)? s;
                try { s = ReadStartAndPgrp(pid); } catch { continue; }
                if (s is not { } info || info.ppid == 0) continue;
                if (!byPpid.TryGetValue(info.ppid, out var list)) byPpid[info.ppid] = list = new();
                list.Add((info.pid, info.starttime, info.pgrp));
            }
            var queue = new Queue<int>(roots);
            var visited = new HashSet<int>(roots);
            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                if (!byPpid.TryGetValue(p, out var kids) || kids.Count == 0) continue;
                foreach (var (c, starttime, pgrp) in kids)
                {
                    if (!visited.Add(c)) continue;
                    owned.TryAdd(c, starttime);
                    pgroups.Add(pgrp);
                    queue.Enqueue(c);
                }
            }
        }
        foreach (var pg in pgroups)
        {
            // pi-sharp-job-cancel-nested-process-groups (return): the group lifecycle guard. A group
            // number is freed when its last member leaves and may be reclaimed by an unrelated group
            // before we signal it; kill(-pgid) has no membership check. The guard proves the number
            // still belongs to our observed set (an observed pid+starttime alive in that group now);
            // otherwise the group signal is refused and the per-pid starttime kills still cover every
            // observed member individually. Residual, documented: a foreign process that joins a
            // still-owned group via setpgid(2) before the signal rides our kill; closing that would
            // require abandoning group signaling (architecture change, reported, not done).
            if (!GroupStillOwned(pg, owned)) continue;
            try { Kill(-pg, 9); } catch { /* group already gone */ }
        }
        foreach (var (pid, starttime) in owned)
        {
            try
            {
                if (!PidMatchesObserved(pid, starttime)) continue; // pid recycled; not ours
                Kill(pid, 9);
            }
            catch { /* already gone */ }
        }
        try { process.Kill(); } catch { /* root already dead */ }
        return true;
    }

    // pi-sharp-job-cancel-nested-process-groups (return): the root-gone fallback. When the root has
    // exited and left no live reparented child, the ppid walk is blind (the orphans sit under init),
    // but the output pipes are private: the runtime creates them for this command, the shell hands
    // them to the child, and only a descendant of this command can hold a write end. Only fds whose
    // /proc/<pid>/fdinfo flags show write access (O_WRONLY/O_RDWR) prove that: a foreign process
    // that holds just a read end never qualifies and is never signalled. Each qualifier is
    // collected with its start time and pgrp and the caller's kill loop does the signalling.
    // Returns false when no pipe target could be resolved, so the caller keeps the legacy behavior.
    private static bool PipeHolders(Process process, Dictionary<int, long> owned, HashSet<int> pgroups)
    {
        var targets = new HashSet<string>();
        foreach (Stream? stream in new Stream?[] { process.StandardOutput?.BaseStream, process.StandardError?.BaseStream })
        {
            if (stream is null) continue;
            var target = FdTarget(PipeFd(stream) is { } fd ? $"/proc/self/fd/{fd}" : null);
            if (target is not null && target.StartsWith("pipe:[", StringComparison.Ordinal)) targets.Add(target);
        }
        if (targets.Count == 0) return false;

        var self = Environment.ProcessId;
        foreach (var entry in Directory.EnumerateDirectories("/proc"))
        {
            var name = Path.GetFileName(entry);
            if (!int.TryParse(name, out var pid) || pid == self) continue;
            (int pid, int ppid, long starttime, int pgrp)? info;
            try { info = ReadStartAndPgrp(pid); } catch { continue; }
            if (info is not { } i) continue;
            try
            {
                foreach (var fdPath in Directory.EnumerateFiles($"/proc/{pid}/fd"))
                {
                    var fd = Path.GetFileName(fdPath);
                    var link = FdTarget(fdPath);
                    // Write access on our pipe inode (fdinfo flags O_WRONLY/O_RDWR) is the ownership
                    // proof; a read-only holder of one of our pipe inodes is not a descendant.
                    if (link is not null && targets.Contains(link) && FdHasWriteAccess($"/proc/{pid}/fdinfo/{fd}"))
                    {
                        owned.TryAdd(pid, i.starttime);
                        pgroups.Add(i.pgrp);
                        break;
                    }
                }
            }
            catch { continue; } // e.g. /proc/1/fd (init) is EACCES-protected: skip, not ours anyway
        }
        return true;
    }

    // The reader-side fd of a redirected process stream. The runtime keeps the SafeHandle in the
    // internal PipeStream._handle field, so it is read reflectively; a failure at any step just
    // means the pipe target is unknown and the caller falls back to the legacy behavior.
    private static int? PipeFd(Stream stream)
    {
        try
        {
            // _handle is a .NET runtime implementation detail (PipeStream keeps the SafeHandle
            // there); reflectively reading it is intentionally un-trim-safe, and any failure just
            // means the pipe target is unknown, in which case the caller keeps the legacy behavior.
#pragma warning disable IL2075
            var field = stream.GetType().GetField("_handle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?? stream.GetType().BaseType?.GetField("_handle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
#pragma warning restore IL2075
            var handle = field?.GetValue(stream) as SafeHandle;
            if (handle is null) return null;
            return handle.DangerousGetHandle().ToInt32();
        }
        catch { return null; }
    }

    // pi-sharp-job-cancel-nested-process-groups (return): the group lifecycle guard, as a seam so the
    // refusal is unit-testable: at least one observed (pid, kernel start time) member must be alive in
    // exactly this group right now, or the number is empty/reused and the group signal is refused.
    internal static bool GroupStillOwned(int pgid, Dictionary<int, long> observed)
    {
        foreach (var entry in Directory.EnumerateDirectories("/proc"))
        {
            var name = Path.GetFileName(entry);
            if (!int.TryParse(name, out var pid)) continue;
            (int pid, int ppid, long starttime, int pgrp)? s;
            try { s = ReadStartAndPgrp(pid); } catch { continue; }
            if (s is not { } info || info.pgrp != pgid) continue;
            if (observed.TryGetValue(pid, out var st) && st == info.starttime) return true;
        }
        return false;
    }

    // pi-sharp-job-cancel-nested-process-groups (return): the access mode of a /proc fd, from its
    // fdinfo "flags" field (the O_* constants from <fcntl.h>): only a write access (O_WRONLY 01
    // or O_RDWR 02) marks the holder as a write-end holder of the pipe. Exposed so the guard is
    // unit-testable (a read-end fd is never counted).
    internal static bool FdHasWriteAccess(string fdinfoPath)
    {
        try
        {
            foreach (var line in File.ReadLines(fdinfoPath))
            {
                if (!line.StartsWith("flags:", StringComparison.Ordinal)) continue;
                var hex = line["flags:".Length..].Trim();
                try
                {
                    long flags = 0;
                    foreach (var c in hex) { if (!Uri.IsHexDigit(c)) break; flags = (flags << 4) + (c - '0' + (c >= 'a' ? c - 'a' + 10 : c >= 'A' ? c - 'A' + 10 : 0)); }
                    return (flags & 0x3) != 0; // O_WRONLY | O_RDWR
                }
                catch { return false; }
            }
            return false;
        }
        catch { return false; } // vanished fd or unreadable (e.g. init): not ours
    }

    // /proc fd symlink target, e.g. "pipe:[12345]". glibc's readlink is versioned-only (not directly
    // resolvable from a DllImport), so the runtime's own wrapper is used.
    private static string? FdTarget(string? path)
    {
        if (path is null) return null;
        try
        {
            var buffer = new byte[256];
            var n = ReadLink(path, buffer, buffer.Length);
            return n > 0 ? Encoding.ASCII.GetString(buffer, 0, n) : null;
        }
        catch { return null; }
    }

    [DllImport("libSystem.Native.so", EntryPoint = "SystemNative_ReadLink")]
    private static extern int ReadLink(string path, byte[] buffer, int size);

    // /proc/<pid>/stat: "pid (comm) state ppid pgrp ..."; the comm may contain spaces and parens, so
    // split after the last ')'. Fields after it: state=0, ppid=1, pgrp=2, starttime=19 (field 22);
    // the start time guards against signalling a recycled pid.
    // pi-sharp-job-cancel-nested-process-groups (return): the per-pid re-verification applied before
    // signalling a collected pid. True only while the process at that pid is the one we observed
    // (same start time), so a recycled pid is never signalled; exposed so the guard is unit-testable.
    internal static bool PidMatchesObserved(int pid, long observedStarttime)
    {
        try { return ReadStartAndPgrp(pid)?.starttime == observedStarttime; }
        catch { return false; } // the /proc entry vanished between enumeration and this check: not ours
    }

    internal static (int pid, int ppid, long starttime, int pgrp)? ReadStartAndPgrp(int pid)
    {
        var stat = File.ReadAllText($"/proc/{pid}/stat");
        var fields = stat[(stat.LastIndexOf(')') + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 20) return null;
        if (!int.TryParse(fields[1], out var ppid) || !long.TryParse(fields[19], out var starttime)) return null;
        return (pid, ppid, starttime, int.Parse(fields[2]));
    }

    private static (int pid, long starttime, int pgrp)? FirstChild(int parent)
    {
        foreach (var entry in Directory.EnumerateDirectories("/proc"))
        {
            var name = Path.GetFileName(entry);
            if (!int.TryParse(name, out var pid)) continue;
            (int pid, int ppid, long starttime, int pgrp)? s;
            try { s = ReadStartAndPgrp(pid); } catch { continue; }
            if (s is { } info && info.ppid == parent) return (info.pid, info.starttime, info.pgrp);
        }
        return null;
    }

    [DllImport("libc", EntryPoint = "setpgid", SetLastError = true)]
    private static extern int SetPgid(int pid, int pgid);

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int Kill(int pid, int sig);
}

/// <summary>
/// pi-sharp-hh-2: a bash command still running at its yield window, offered to the job registry as is.
/// <paramref name="Run"/> completes with the command's own result; <paramref name="Stop"/> kills it;
/// <paramref name="Redirect"/> points its live output at the adopter.
/// </summary>
public sealed record BashYield(
    string Command, string SoFar, Task<AgentToolResult> Run, Func<int?> ExitCode,
    CancellationTokenSource Stop, Action<Action<AgentToolResult>?> Redirect, Func<StreamWriter?>? Stdin = null,
    SessionOutput? Output = null);
