using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PiSharp.Core.Notebook;
using PiSharp.Tools;

namespace PiSharp.Tests.Tools;

public class JobToolTests
{
    // Linux seats have /bin/bash; the Windows dev host runs these through Git Bash.
    private static bool BashAvailable() => File.Exists("/bin/bash") || File.Exists("/usr/bin/bash")
        || (OperatingSystem.IsWindows() && File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe")));

    private static async Task<JsonObject> Run(JobTool tool, JsonObject args, CancellationToken ct = default)
    {
        var result = await tool.ExecuteAsync("t", args, null, ct);
        Assert.False(result.IsError, result.Text);
        return JsonNode.Parse(result.Text)!.AsObject();
    }

    private static async Task<string> Start(JobTool tool, string command, bool stdin = false)
    {
        var started = await Run(tool, new JsonObject { ["op"] = "start", ["command"] = command, ["stdin"] = stdin });
        return started["id"]!.GetValue<string>();
    }

    /// <summary>plan-pi-sharp-codex item 6: many sessions, up to the seat's cap; one more is refused until one ends.</summary>
    [Fact]
    public async Task JobsStartUpToTheCap_AndOneMoreIsRefusedUntilOneEnds()
    {
        if (!BashAvailable()) return;
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Dir("nb"), "seat1");
        await using var tool = new JobTool(tmp.Path, store) { MaxActive = 2 };
        var first = await Start(tool, "sleep 30");
        await Start(tool, "sleep 30");
        var third = await tool.ExecuteAsync("t", new JsonObject { ["op"] = "start", ["command"] = "echo three" }, null, default);
        Assert.True(third.IsError);
        await Run(tool, new JsonObject { ["op"] = "cancel", ["id"] = first });
        await Run(tool, new JsonObject { ["op"] = "wait", ["id"] = first, ["timeoutSeconds"] = 20 });
        Assert.StartsWith("job-", await Start(tool, "echo three"));
    }

    [Fact]
    public async Task WaitReturnsWhenTheJobExitsWithItsExitCodeAndOutput()
    {
        if (!BashAvailable()) return;
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Dir("nb"), "seat1");
        await using var tool = new JobTool(tmp.Path, store);
        var id = await Start(tool, "sleep 1.5; echo finished; exit 3");

        var waited = await Run(tool, new JsonObject { ["op"] = "wait", ["id"] = id, ["timeoutSeconds"] = 60 });
        Assert.Equal("exited", waited["waited"]!.GetValue<string>());
        Assert.Equal(3, waited["exitCode"]!.GetValue<int>());
        Assert.Contains("finished", waited["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task WaitUntilMatchesOnlyOutputProducedAfterTheWaitBegan()
    {
        if (!BashAvailable()) return;
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Dir("nb"), "seat1");
        await using var tool = new JobTool(tmp.Path, store);
        // The second 'ready' comes at ~15 s, well after the wait begins; the pre-wait line is what
        // 'until' must not return for.
        var id = await Start(tool, "echo ready; sleep 15; echo ready; sleep 30");
        await Task.Delay(300);

        var clock = Stopwatch.StartNew();
        var waited = await Run(tool, new JsonObject { ["op"] = "wait", ["id"] = id, ["until"] = "^ready$", ["timeoutSeconds"] = 20 });
        Assert.Equal("matched", waited["waited"]!.GetValue<string>());
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(15), clock.Elapsed.ToString());
        Assert.Contains("ready", waited["liveOutput"]!.GetValue<string>());
        await Run(tool, new JsonObject { ["op"] = "cancel", ["id"] = id });
    }

    // Bounded observation: poll status every 250 ms until the job's liveOutput has at least
    // `expectedReady` '^ready$' lines, then return the count. The caller must assert on the
    // result: on expiry the count is the last one seen and the assertion fails the test.
    // A load budget for the drain pipeline, not part of the oracle.
    private static async Task<int> LiveReadyLineCount(JobTool tool, string id, int expectedReady, int seconds)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        var count = 0;
        while (DateTime.UtcNow < until)
        {
            var live = (await Run(tool, new JsonObject { ["op"] = "status", ["id"] = id }))["liveOutput"]!.GetValue<string>();
            count = Regex.Matches(live, "^ready$", RegexOptions.Multiline).Count;
            if (count >= expectedReady) return count;
            await Task.Delay(250);
        }
        return count;
    }

    [Fact]
    public async Task WaitUntilMatchesOnlyTheLinePublishedAfterTheWaitBaseline()
    {
        if (!BashAvailable()) return;
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Dir("nb"), "seat1");
        await using var tool = new JobTool(tmp.Path, store);
        // Producer (frozen tl-third-sentinel-v3-freeze-0125): line 2 is published only when the
        // test creates ./sentinel (ordinary file, existence-only: nothing is forwarded into the
        // output; no FIFO, no OS-blocked write); line 3 lands 30 s after the sentinel; the
        // producer is bounded and killed by the final cancel. Absent-sentinel exhaustion exits
        // after line 1 only - never line 2.
        var id = await Start(tool, "echo ready; for i in $(seq 1 300); do [ -f sentinel ] && break; sleep 0.2; done; [ -f sentinel ] && { echo ready; sleep 30; echo ready; }");
        var sentinel = Path.Combine(tmp.Path, "sentinel");
        try
        {
            // Exactly one old line before the wait starts: line 1 has drained and line 2
            // cannot exist yet (no sentinel); the result is asserted, so an expired poll
            // budget fails here instead of falling through to the wait.
            Assert.Equal(1, await LiveReadyLineCount(tool, id, 1, 30));

            // Source pin (Proof A): the wait op is synchronous until `await WaitAsync`
            // (JobTool.cs:101); WaitAsync takes `var baseline = job.Output` (:195) before
            // its first real await (Task.WhenAny, :205); it matches only via
            // NewOutput(baseline, job.Output) (:203, :207). The un-awaited Run(...) below
            // returns its Task only after the baseline snapshot, so the sentinel written
            // here is strictly post-baseline.
            var waitTask = Run(tool, new JsonObject { ["op"] = "wait", ["id"] = id, ["until"] = "^ready$", ["timeoutSeconds"] = 15 });
            File.WriteAllText(sentinel, "publish");
            var first = await waitTask;
            Assert.Equal("matched", first["waited"]!.GetValue<string>());

            // Old-only control: lines 1-2 already sit in the job's output before this wait's
            // baseline; line 3 comes 30 s after the sentinel, past this 3 s window. A wait
            // that matched the whole output instead of NewOutput would return "matched" at
            // once; "timeout" certifies the producer is alive in the window (Proof B).
            var second = await Run(tool, new JsonObject { ["op"] = "wait", ["id"] = id, ["until"] = "^ready$", ["timeoutSeconds"] = 3 });
            Assert.Equal("timeout", second["waited"]!.GetValue<string>());
        }
        finally
        {
            await Run(tool, new JsonObject { ["op"] = "cancel", ["id"] = id });
            if (File.Exists(sentinel)) File.Delete(sentinel);
        }
    }

    /// <summary>digital-scribe, 2026-10-19: "yes, fix the job tool limit to 2 hours". A full suite on a loaded box needs more
    /// than the old 1200 s cap, and a job stopped at its lifetime says so.</summary>
    [Fact]
    public void AJobMayLiveTwoHoursAndItsSchemaAndDescriptionSaySo()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Dir("nb"), "seat1");
        var tool = new JobTool(tmp.Path, store);
        Assert.Equal(7200, JobTool.MaxLifetimeSeconds);
        Assert.Equal(JobTool.MaxLifetimeSeconds, tool.Parameters["properties"]!["timeout"]!["maximum"]!.GetValue<int>());
        Assert.Contains("max 7200 s, two hours", tool.Description);
        Assert.DoesNotContain("one hour", tool.Description);
    }

    [Fact]
    public async Task AJobStoppedAtItsLifetimeSaysSo()
    {
        if (!BashAvailable()) return;
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Dir("nb"), "seat1");
        await using var tool = new JobTool(tmp.Path, store);
        var started = await Run(tool, new JsonObject { ["op"] = "start", ["command"] = "echo started; sleep 30", ["timeout"] = 2 });
        var id = started["id"]!.GetValue<string>();

        var waited = await Run(tool, new JsonObject { ["op"] = "wait", ["id"] = id, ["timeoutSeconds"] = 30 });
        Assert.Equal("exited", waited["waited"]!.GetValue<string>());
        var text = waited["text"]!.GetValue<string>();
        Assert.Contains("Command timed out after 2 seconds", text);
        Assert.Contains("the job reached its lifetime and was stopped", text);
    }

    [Fact]
    public async Task WaitTimesOutAndIsCancellableByTheTurn()
    {
        if (!BashAvailable()) return;
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Dir("nb"), "seat1");
        await using var tool = new JobTool(tmp.Path, store);
        var id = await Start(tool, "sleep 30");

        var timedOut = await Run(tool, new JsonObject { ["op"] = "wait", ["id"] = id, ["timeoutSeconds"] = 1 });
        Assert.Equal("timeout", timedOut["waited"]!.GetValue<string>());

        using var turn = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            tool.ExecuteAsync("t", new JsonObject { ["op"] = "wait", ["id"] = id }, null, turn.Token));
        await Run(tool, new JsonObject { ["op"] = "cancel", ["id"] = id });
    }

    [Fact]
    public async Task InputWritesToTheJobsStdin()
    {
        if (!BashAvailable()) return;
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Dir("nb"), "seat1");
        await using var tool = new JobTool(tmp.Path, store);
        var id = await Start(tool, "read -r line; echo \"got:$line\"; cat; echo done", stdin: true);

        await Run(tool, new JsonObject { ["op"] = "input", ["id"] = id, ["text"] = "hello" });
        await Run(tool, new JsonObject { ["op"] = "input", ["id"] = id, ["text"] = "tail", ["eof"] = true });
        var waited = await Run(tool, new JsonObject { ["op"] = "wait", ["id"] = id, ["timeoutSeconds"] = 30 });
        Assert.Equal("exited", waited["waited"]!.GetValue<string>());
        var text = waited["text"]!.GetValue<string>();
        Assert.Contains("got:hello", text);
        Assert.Contains("tail", text);
        Assert.Contains("done", text);
    }

    [Fact]
    public async Task InputIsRefusedForAJobWithoutStdin()
    {
        if (!BashAvailable()) return;
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Dir("nb"), "seat1");
        await using var tool = new JobTool(tmp.Path, store);
        var id = await Start(tool, "sleep 30");
        var refused = await tool.ExecuteAsync("t", new JsonObject { ["op"] = "input", ["id"] = id, ["text"] = "x" }, null, default);
        Assert.True(refused.IsError);
        Assert.Contains("stdin=true", refused.Text);
        await Run(tool, new JsonObject { ["op"] = "cancel", ["id"] = id });
    }

    [Theory]
    [InlineData("", "abc", "abc")]
    [InlineData("abc", "abcdef", "def")]
    [InlineData("xxabc", "abcdef", "def")]
    [InlineData("zzz", "abcdef", "abcdef")]
    public void NewOutputFollowsASlidingTail(string baseline, string current, string expected) =>
        Assert.Equal(expected, JobTool.NewOutput(baseline, current));

    [Fact]
    public void TailKeepsHeadAndRecentOutputWithOmissionCounts()
    {
        var output = JobTool.Tail("FIRST\n" + new string('x', 10_000) + "\nLAST");
        Assert.StartsWith("FIRST\n", output);
        Assert.Contains("[...", output);
        Assert.EndsWith("LAST", output);
    }
}
