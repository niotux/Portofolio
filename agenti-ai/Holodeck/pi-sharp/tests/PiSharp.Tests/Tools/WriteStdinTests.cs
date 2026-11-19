using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PiSharp.Core.Agent;
using PiSharp.Core.Notebook;
using PiSharp.Tools;

namespace PiSharp.Tests.Tools;

// plan-pi-sharp-codex item 6 (Codex unified exec): a terminal command that yields becomes a session the model types into with
// write_stdin; output comes back cut to a token budget with its original size said.
public sealed class WriteStdinTests
{
    private static string JobIdFrom(string text) =>
        Regex.Match(text, @"^job_id=(job-[0-9a-f]{32})$", RegexOptions.Multiline).Value["job_id=".Length..];

    private static async Task<JsonObject> Write(WriteStdinTool tool, string id, string chars, int yieldMs = 3000)
    {
        var result = await tool.ExecuteAsync("w", new JsonObject { ["session_id"] = id, ["chars"] = chars, ["yield_time_ms"] = yieldMs }, null, default);
        Assert.False(result.IsError, string.Concat(result.Content.OfType<PiSharp.Core.Messages.TextContent>().Select(t => t.Text)));
        return JsonNode.Parse(string.Concat(result.Content.OfType<PiSharp.Core.Messages.TextContent>().Select(t => t.Text)))!.AsObject();
    }

    [Fact]
    public async Task ASeatDrivesAnInteractiveCommandThroughWriteStdin()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists(BashTool.ScriptPath)) return;
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Dir("nb"), "seat1");
        await using var jobs = new JobTool(tmp.Path, store);
        var bash = new BashTool(tmp.Path) { YieldSink = y => Task.FromResult(jobs.Adopt(y, "wi-test")) };
        var stdin = new WriteStdinTool(jobs);

        // A prompt that waits for a person: it yields after a second, still asking.
        var started = await ToolRun.Exec(bash, """{"command":"read -p 'name? ' n; read -p 'colour? ' c; echo \"hello $n in $c\"","tty":true,"yield":1}""");
        Assert.Contains("name?", started.Text);
        var id = JobIdFrom(started.Text);

        var first = await Write(stdin, id, "world\n");
        Assert.Contains("colour?", first["output"]!.GetValue<string>());
        Assert.True(first["running"]!.GetValue<bool>());

        var second = await Write(stdin, id, "blue\n");
        Assert.Contains("hello world in blue", second["output"]!.GetValue<string>());
        Assert.False(second["running"]!.GetValue<bool>());
        Assert.Equal(0, second["exit_code"]!.GetValue<int>());

        var ended = await stdin.ExecuteAsync("x", new JsonObject { ["session_id"] = id, ["chars"] = "more\n" }, null, default);
        Assert.True(ended.IsError);
    }

    [Fact]
    public async Task ACommandWithoutATerminalTakesNoInput_AndAnEmptyWriteOnlyReads()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Dir("nb"), "seat1");
        await using var jobs = new JobTool(tmp.Path, store);
        var bash = new BashTool(tmp.Path) { YieldSink = y => Task.FromResult(jobs.Adopt(y, "wi-test")) };
        var stdin = new WriteStdinTool(jobs);
        var started = await ToolRun.Exec(bash, """{"command":"echo one; sleep 2; echo two; sleep 30","yield":1}""");
        var id = JobIdFrom(started.Text);
        var refused = await stdin.ExecuteAsync("x", new JsonObject { ["session_id"] = id, ["chars"] = "y\n" }, null, default);
        Assert.True(refused.IsError);
        Assert.Contains("tty=true", string.Concat(refused.Content.OfType<PiSharp.Core.Messages.TextContent>().Select(t => t.Text)));
        var read = await Write(stdin, id, "", 4000);
        Assert.Contains("two", read["output"]!.GetValue<string>());
        Assert.DoesNotContain("one", read["output"]!.GetValue<string>()); // only what came since
        await jobs.ExecuteAsync("c", new JsonObject { ["op"] = "cancel", ["id"] = id }, null, default);
    }

    [Fact]
    public void OutputIsCutInTheMiddleToItsTokenBudget_WithTheOriginalSizeSaid()
    {
        Assert.Equal("short", OutputCap.MiddleCut("short", 100));
        var text = string.Concat(Enumerable.Range(0, 2000).Select(i => $"line {i:0000}\n"));
        var cut = OutputCap.MiddleCut(text, 100);
        Assert.StartsWith("Original output: about 5000 tokens, 2000 lines; cut to 100.", cut);
        Assert.Contains("line 0000", cut);
        Assert.Contains("line 1999", cut);
        Assert.DoesNotContain("line 1000", cut);
        Assert.Contains("tokens left out…", cut);
        Assert.True(cut.Length < 600);
    }

    [Fact]
    public async Task BashCutsItsOutputToMaxOutputTokens_AFailedCommandToo()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var tmp = new TempWorkspace();
        var bash = new BashTool(tmp.Path);
        var ok = await ToolRun.Exec(bash, """{"command":"seq 1 5000","max_output_tokens":50}""");
        Assert.Contains("Original output: about", ok.Text);
        Assert.Contains("\n1\n", "\n" + ok.Text);
        Assert.Contains("5000", ok.Text);
        var failed = await bash.ExecuteAsync("f", JsonNode.Parse("""{"command":"seq 1 5000; exit 3","max_output_tokens":50}""")!.AsObject(), null, default)
            .ContinueWith(t => t.Exception?.GetBaseException().Message ?? string.Concat(t.Result.Content.OfType<PiSharp.Core.Messages.TextContent>().Select(c => c.Text)));
        Assert.Contains("Original output: about", failed);
        Assert.True(failed.Length < 1000, failed.Length.ToString());
    }
}
