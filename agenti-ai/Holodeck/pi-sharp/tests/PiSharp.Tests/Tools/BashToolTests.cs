using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PiSharp.Core.Agent;
using PiSharp.Tools;

namespace PiSharp.Tests.Tools;

public class BashToolTests
{
    private static bool BashAvailable() => File.Exists("/bin/bash") || File.Exists("/usr/bin/bash");

    [Fact]
    public async Task CapturesStdoutAndStderrMerged()
    {
        if (!BashAvailable()) return;
        using var tmp = new TempWorkspace();
        var result = await ToolRun.Exec(new BashTool(tmp.Path), """{"command":"echo out; echo err >&2"}""");
        Assert.Contains("out", result.Text);
        Assert.Contains("err", result.Text);
    }

    [Fact]
    public async Task NonZeroExitIncludesCode()
    {
        if (!BashAvailable()) return;
        using var tmp = new TempWorkspace();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ToolRun.Exec(new BashTool(tmp.Path), """{"command":"echo failed; exit 7"}"""));
        Assert.Contains("failed", ex.Message);
        Assert.Contains("Command exited with code 7", ex.Message);
    }

    [Theory]
    [InlineData("Cloning into 'x'...\nfatal: unable to access 'https://gitlab.teetin.ro/devs/pi-sharp.git/': Could not resolve host: gitlab.teetin.ro", "https://gitlab.teetin.ro/devs/pi-sharp.git")]
    [InlineData("remote: The project you were looking for could not be found.\nfatal: repository 'https://gitlab.teletin.ro/devs/pisharp.git/' not found", "https://gitlab.teletin.ro/devs/pisharp.git")]
    [InlineData("curl: (6) Could not resolve host: gitlab.teetin.ro", "gitlab.teetin.ro")]
    [InlineData("ssh: Could not resolve hostname gitlab.teetin.ro: Name or service not known\nfatal: Could not read from remote repository.", "gitlab.teetin.ro")]
    [InlineData("fatal: 'factory:devs/pi-sharp' does not appear to be a git repository", "factory:devs/pi-sharp")]
    public void FailedName_IsTheNameTheCommandCouldNotReach(string output, string expected) =>
        Assert.Equal(expected, BashTool.FailedName(output));

    [Theory]
    [InlineData("error CS1002: ; expected")]
    [InlineData("Failed: 3, Passed: 180")]
    public void FailedName_IsNullForAnyOtherFailure(string output) => Assert.Null(BashTool.FailedName(output));

    [Fact]
    public async Task AFailedHost_GetsTheKbHint_AndOtherFailuresDoNot()
    {
        if (!BashAvailable()) return;
        using var tmp = new TempWorkspace();
        var asked = new List<string>();
        var tool = new BashTool(tmp.Path) { NameHint = (name, _) => { asked.Add(name); return Task.FromResult<string?>("did you mean gitlab.teletin.ro?"); } };
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ToolRun.Exec(tool, """{"command":"echo 'curl: (6) Could not resolve host: gitlab.teetin.ro'; exit 6"}"""));
        Assert.Contains("KB names: did you mean gitlab.teletin.ro?", ex.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ToolRun.Exec(tool, """{"command":"echo nope; exit 1"}"""));
        Assert.Equal(["gitlab.teetin.ro"], asked);
    }

    [Fact]
    public async Task AHangingKb_DoesNotHoldTheFailure()
    {
        if (!BashAvailable()) return;
        using var tmp = new TempWorkspace();
        var tool = new BashTool(tmp.Path) { NameHint = async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return null; } };
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ToolRun.Exec(tool, """{"command":"echo 'Could not resolve host: x.test'; exit 6"}"""));
        Assert.Contains("Command exited with code 6", ex.Message);
        Assert.DoesNotContain("KB names", ex.Message);
    }

    [Fact]
    public async Task TimeoutKillsAndReports()
    {
        if (!BashAvailable()) return;
        using var tmp = new TempWorkspace();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ToolRun.Exec(new BashTool(tmp.Path), """{"command":"sleep 30","timeout":0.4}"""));
        Assert.Contains("Command timed out after 0.4 seconds", ex.Message);
    }

    [Fact]
    public void OmittedTimeoutHasADefault()
    {
        using var tmp = new TempWorkspace();
        var tool = new BashTool(tmp.Path);
        Assert.Contains("default 600", tool.Parameters["properties"]!["timeout"]!["description"]!.GetValue<string>());
        Assert.Equal(600, BashTool.DefaultTimeoutSeconds);
    }

    // pi-sharp-hh-2: a command still running at the yield window returns with a job id instead of blocking the turn.
    private static Task<AgentToolResult?> PromoteVia(JobTool jobs, BashYield running) =>
        Task.FromResult(jobs.Adopt(running, "wi-test"));

    private static string JobIdFrom(string text) =>
        Regex.Match(text, @"^job_id=(job-[0-9a-f]{32})$", RegexOptions.Multiline).Value.ToString()["job_id=".Length..];

    // pi-sharp-hh-2-fix: 0.5.51 killed a yielded command and re-ran it as a job, whose own shell then
    // yielded and was SIGKILLed at 30 s (exit 137). No test ran anything longer than the window.
    [Fact]
    public async Task AJobOutlivesTheYieldWindow()
    {
        if (!BashAvailable()) return;
        using var tmp = new TempWorkspace();
        using var store = new PiSharp.Core.Notebook.NotebookStore(tmp.Dir("nb"), "seat1");
        await using var jobs = new JobTool(tmp.Path, store);
        var started = await jobs.ExecuteAsync("s", new JsonObject
            { ["op"] = "start", ["command"] = $"sleep {BashTool.DefaultYieldSeconds + 5}; echo done", ["task"] = "wi-test" }, null, default);
        Assert.False(started.IsError, started.Text);
        var id = JsonNode.Parse(started.Text)!["id"]!.GetValue<string>();
        var waited = JsonNode.Parse((await jobs.ExecuteAsync("w", new JsonObject
            { ["op"] = "wait", ["id"] = id, ["timeoutSeconds"] = 120 }, null, default)).Text)!.AsObject();
        Assert.Equal("exited", waited["waited"]!.GetValue<string>());
        Assert.Equal(0, waited["exitCode"]!.GetValue<int>());
        Assert.Contains("done", waited["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task AYieldedCommandRunsOnceAndPastTheWindow()
    {
        if (!BashAvailable()) return;
        using var tmp = new TempWorkspace();
        using var store = new PiSharp.Core.Notebook.NotebookStore(tmp.Dir("nb"), "seat1");
        await using var jobs = new JobTool(tmp.Path, store);
        var tool = new BashTool(tmp.Path) { YieldSink = y => PromoteVia(jobs, y) };

        var result = await ToolRun.Exec(tool, """{"command":"echo run >> runs.txt; echo part1; sleep 8; echo done","yield":5}""");
        Assert.Contains("part1", result.Text);
        var id = JobIdFrom(result.Text);
        var waited = JsonNode.Parse((await jobs.ExecuteAsync("w", new JsonObject
            { ["op"] = "wait", ["id"] = id, ["timeoutSeconds"] = 60 }, null, default)).Text)!.AsObject();
        Assert.Equal("exited", waited["waited"]!.GetValue<string>());
        Assert.Equal(0, waited["exitCode"]!.GetValue<int>());
        Assert.Contains("done", waited["text"]!.GetValue<string>());
        Assert.Equal(["run"], File.ReadAllLines(Path.Combine(tmp.Path, "runs.txt")));
        Assert.Contains("\"origin\":\"bash-yield\"", waited["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task WithoutAYieldSinkBashNeverYields()
    {
        if (!BashAvailable()) return;
        using var tmp = new TempWorkspace();
        var result = await ToolRun.Exec(new BashTool(tmp.Path), """{"command":"sleep 2; echo done","yield":1}""");
        Assert.Contains("done", result.Text);
        Assert.DoesNotContain("job_id", result.Text);
    }
    [Fact]
    public async Task YieldReturnsAJobIdInsteadOfBlockingTheTurn()
    {
        if (!BashAvailable()) return;
        using var tmp = new TempWorkspace();
        using var store = new PiSharp.Core.Notebook.NotebookStore(tmp.Dir("nb"), "seat1");
        await using var jobs = new JobTool(tmp.Path, store) { MaxActive = 2 }; // this test pins a cap of two
        string? promotedCommand = null;
        var tool = new BashTool(tmp.Path)
        {
            YieldSink = y => { promotedCommand = y.Command; return PromoteVia(jobs, y); },
        };

        var clock = Stopwatch.StartNew();
        var result = await ToolRun.Exec(tool, """{"command":"echo part1; sleep 32; echo done","yield":6}""");
        clock.Stop();
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), clock.Elapsed.ToString());
        Assert.Contains("part1", result.Text);
        Assert.DoesNotContain("done", result.Text);
        var jobMatch = Regex.Matches(result.Text, @"^job_id=(job-[0-9a-f]{32})$", RegexOptions.Multiline);
        Assert.Single(jobMatch);
        var id = jobMatch[0].Groups[1].Value;
        Assert.Equal("echo part1; sleep 32; echo done", promotedCommand);

        // While job1 still runs, the 2-job cap: a second promotion takes the last slot, and anything
        // else - a plain start or a third yield - is refused clearly. All of this while both jobs run,
        // because waiting on job1 below would free its slot.
        var t2 = new BashTool(tmp.Path) { YieldSink = y => PromoteVia(jobs, y) };
        var t3 = new BashTool(tmp.Path) { YieldSink = y => PromoteVia(jobs, y) };
        var second = await ToolRun.Exec(t2, """{"command":"sleep 30","yield":2}""");
        Assert.Contains("job_id=job-", second.Text);
        var secondId = JobIdFrom(second.Text);
        var third = await jobs.ExecuteAsync("c2", new JsonObject
            { ["op"] = "start", ["command"] = "echo never", ["task"] = "wi-test", ["origin"] = "bash-yield" }, null, default);
        Assert.True(third.IsError);
        Assert.Contains("free job slot", third.Text);
        // A third concurrent promotion through the yield sink is refused the same way: the registry
        // reports no free slot, the sink returns null, and bash says so clearly (and stops the command).
        var ex3 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ToolRun.Exec(t3, """{"command":"sleep 30","yield":2}"""));
        Assert.Contains("no free job slot", ex3.Message);

        // The promoted job later shows the full output once it exits.
        var waited = await jobs.ExecuteAsync("w", new JsonObject { ["op"] = "wait", ["id"] = id, ["timeoutSeconds"] = 60 }, null, default);
        Assert.False(waited.IsError, waited.Text);
        var record = JsonNode.Parse(waited.Text)!.AsObject();
        Assert.Equal("exited", record["waited"]!.GetValue<string>());
        Assert.Contains("done", record["text"]!.GetValue<string>());

        // Cancel the second promoted job; its evidence row is written on completion.
        await jobs.ExecuteAsync("c3", new JsonObject { ["op"] = "cancel", ["id"] = secondId }, null, default);
    }
    [Fact]
    public async Task ATimeoutShorterThanTheYieldIsHonoredAsATimeout()
    {
        if (!BashAvailable()) return;
        using var tmp = new TempWorkspace();
        var tool = new BashTool(tmp.Path) { YieldSink = _ => throw new InvalidOperationException("yield must not be reached") };
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ToolRun.Exec(tool, """{"command":"sleep 30","timeout":0.4,"yield":2}"""));
        Assert.Contains("Command timed out after 0.4 seconds", ex.Message);
    }

    [Fact]
    public async Task AFastCommandDoesNotYield()
    {
        if (!BashAvailable()) return;
        using var tmp = new TempWorkspace();
        var tool = new BashTool(tmp.Path) { YieldSink = _ => Task.FromResult<AgentToolResult?>(null) };
        var result = await ToolRun.Exec(tool, """{"command":"echo quick","yield":5}""");
        Assert.Contains("quick", result.Text);
        Assert.DoesNotContain("job_id", result.Text);
    }

    [Fact]
    public async Task YieldWindowIsClampedTo60Seconds()
    {
        if (!BashAvailable()) return;
        using var tmp = new TempWorkspace();
        var tool = new BashTool(tmp.Path) { YieldSink = _ => Task.FromResult<AgentToolResult?>(null) };
        // A 90-second command with yield=500 (clamped to 60) must still finish inside the 60 s window.
        var clock = Stopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ToolRun.Exec(tool, """{"command":"sleep 90","yield":500}"""));
        clock.Stop();
        Assert.Contains("no free job slot", ex.Message);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(75), clock.Elapsed.ToString());
    }

    [Fact]
    public void TimeoutAbove3600IsClampedAndReported()
    {
        using var tmp = new TempWorkspace();
        // The clamp is visible at the call site (BashTool.ResolveTimeoutMs), and the schema says so.
        var tool = new BashTool(tmp.Path);
        Assert.Contains("3600 are clamped", tool.Parameters["properties"]!["timeout"]!["description"]!.GetValue<string>());
        Assert.Equal(3600, BashTool.MaxAllowedTimeoutSeconds);
        Assert.Contains("default 30", tool.Parameters["properties"]!["yield"]!["description"]!.GetValue<string>());
        Assert.Equal(30, BashTool.DefaultYieldSeconds);
    }

    [Fact]
    public async Task AbortReportsAborted()
    {
        if (!BashAvailable()) return;
        using var tmp = new TempWorkspace();
        using var cts = new CancellationTokenSource();
        var task = ToolRun.Exec(new BashTool(tmp.Path), """{"command":"sleep 30"}""", cts.Token);
        await Task.Delay(150);
        cts.Cancel();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        Assert.Contains("Command aborted", ex.Message);
    }

    [Fact]
    public async Task EmptyOutput()
    {
        if (!BashAvailable()) return;
        using var tmp = new TempWorkspace();
        var result = await ToolRun.Exec(new BashTool(tmp.Path), """{"command":"true"}""");
        Assert.Equal("(no output)", result.Text);
    }

    [Fact]
    public async Task InvalidTimeout()
    {
        using var tmp = new TempWorkspace();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ToolRun.Exec(new BashTool(tmp.Path), """{"command":"true","timeout":0}"""));
        Assert.Contains("Invalid timeout", ex.Message);
    }

    [Fact]
    public async Task MissingCwd()
    {
        if (!BashAvailable()) return;
        var tool = new BashTool(Path.Combine(Path.GetTempPath(), "pi-sharp-no-such-" + Guid.NewGuid().ToString("N")));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ToolRun.Exec(tool, """{"command":"echo hi"}"""));
        Assert.Contains("Working directory does not exist", ex.Message);
    }

    [Fact]
    public async Task TruncationWritesTempFile()
    {
        if (!BashAvailable()) return;
        using var tmp = new TempWorkspace();
        // 60KB of output exceeds the 50KB byte limit.
        var result = await ToolRun.Exec(new BashTool(tmp.Path), """{"command":"dd if=/dev/zero bs=60000 count=1 2>/dev/null | tr '\\0' x; echo"}""");
        Assert.NotNull(result.Details);
        Assert.True((bool)result.Details!["truncation"]!["truncated"]!);
        var fullPath = result.Details["fullOutputPath"]!.GetValue<string>();
        Assert.True(File.Exists(fullPath));
        Assert.True(new FileInfo(fullPath).Length >= 50_000);
        try { File.Delete(fullPath); } catch { /* temp */ }
    }

    [Fact]
    public async Task TruncationKeepsHeadAndTailAndReportsOmittedCountsAndPath()
    {
        if (!BashAvailable()) return;
        using var tmp = new TempWorkspace();
        var result = await ToolRun.Exec(new BashTool(tmp.Path), """{"command":"printf 'FIRST\\n'; head -c 10000000 /dev/zero | tr '\\0' x; printf '\\nLAST\\n'"}""");
        var text = result.Text;
        Assert.StartsWith("FIRST\n", text);
        Assert.Contains("LAST\n", text);
        Assert.Contains("[... ", text);
        Assert.Contains(" lines / ", text);
        Assert.Contains(" bytes omitted; full output: ", text);
        var fullPath = result.Details!["fullOutputPath"]!.GetValue<string>();
        Assert.Contains(fullPath, text);
        Assert.True(File.Exists(fullPath));
        try { File.Delete(fullPath); } catch { /* temp */ }
    }
}
