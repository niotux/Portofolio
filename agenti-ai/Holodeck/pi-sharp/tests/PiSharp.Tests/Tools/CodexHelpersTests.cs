using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Messages;
using PiSharp.Core.Models;
using PiSharp.Tests.Agent;
using PiSharp.Tools;

namespace PiSharp.Tests.Tools;

// plan-pi-sharp-codex phase 5: helpers (Codex subagents) and the branch review (Codex /review).
public sealed class CodexHelpersTests
{
    private static ModelConfig Sub => Fakes.Model with { Provider = "openai-codex", Connection = "c" };

    [Fact]
    public async Task ATriageHelperRunsAndSummarises_ButCannotEdit()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var ws = new TempWorkspace();
        var stream = new ScriptedStream()
            .Then(Fakes.ToolCalls(Fakes.Call("h1", "bash", new JsonObject { ["command"] = "echo 'FAILED t1: expected 4 got 5'" })))
            .Then(Fakes.ToolCalls(Fakes.Call("h2", "bash", new JsonObject { ["command"] = "touch fixed.txt" })))
            .Then(Fakes.Text("1 failed: t1, expected 4 got 5 (src/a.cs:12)"));
        var tool = new TaskTool(ws.Path, () => (Sub, stream.Stream), () => "task-1");
        var result = await tool.ExecuteAsync("c", new JsonObject { ["role"] = "triage", ["task"] = "run the tests" }, null, CancellationToken.None);
        Assert.Contains("(triage) finished", Fakes.ResultText(new ToolResultMessage { ToolCallId = "c", ToolName = "task", Content = result.Content }));
        Assert.Contains("1 failed: t1", ((TextContent)result.Content[0]).Text);
        Assert.False(File.Exists(Path.Combine(ws.Path, "fixed.txt"))); // the read-only rules refused the touch
        Assert.Contains("read-only", stream.Contexts[2].Messages.OfType<ToolResultMessage>().Last().Content.OfType<TextContent>().Single().Text);
    }

    /// <summary>plan-pi-sharp-codex item 16: helpers in the background (spawn, message, wait, close), up to three at once and
    /// one writer; a second writer is refused while the first runs.</summary>
    [Fact]
    public async Task HelpersRunInTheBackground_UpToTheCap_WithOneWriter()
    {
        using var ws = new TempWorkspace();
        var gate = new TaskCompletionSource();
        var scripted = new ScriptedStream().Then(Fakes.Text("first done")).Then(Fakes.Text("second done")).Then(Fakes.Text("third done"));
        async IAsyncEnumerable<AssistantMessageEvent> Held(IAsyncEnumerable<AssistantMessageEvent> inner, [EnumeratorCancellation] CancellationToken ct)
        {
            await gate.Task.WaitAsync(ct);
            await foreach (var e in inner.WithCancellation(ct)) yield return e;
        }
        var tool = new TaskTool(ws.Path, () => (Sub, (m, c, o, ct) => Held(scripted.Stream(m, c, o, ct), ct)), () => "task-1");
        string Text(AgentToolResult r) => string.Concat(r.Content.OfType<TextContent>().Select(t => t.Text));
        string Id(AgentToolResult r) => r.Details!["subRunId"]!.GetValue<string>();

        var worker = await tool.ExecuteAsync("c1", new JsonObject { ["op"] = "spawn", ["role"] = "worker", ["task"] = "edit a.txt" }, null, CancellationToken.None);
        Assert.Contains("started in the background", Text(worker));
        var secondWorker = await tool.ExecuteAsync("c2", new JsonObject { ["op"] = "spawn", ["role"] = "worker", ["task"] = "edit b.txt" }, null, CancellationToken.None);
        Assert.True(secondWorker.IsError);
        Assert.Contains("another helper that edits files", Text(secondWorker));
        var triage = await tool.ExecuteAsync("c3", new JsonObject { ["op"] = "spawn", ["role"] = "triage", ["task"] = "run the tests" }, null, CancellationToken.None);
        var triage2 = await tool.ExecuteAsync("c4", new JsonObject { ["op"] = "spawn", ["role"] = "triage", ["task"] = "read the log" }, null, CancellationToken.None);
        Assert.False(triage2.IsError);
        var fourth = await tool.ExecuteAsync("c5", new JsonObject { ["op"] = "spawn", ["role"] = "triage", ["task"] = "one more" }, null, CancellationToken.None);
        Assert.Contains("3 helpers are already running", Text(fourth));

        var told = await tool.ExecuteAsync("c6", new JsonObject { ["op"] = "message", ["id"] = Id(triage), ["text"] = "only the App tests" }, null, CancellationToken.None);
        Assert.Contains("reads it before its next step", Text(told));
        var notYet = await tool.ExecuteAsync("c7", new JsonObject { ["op"] = "wait", ["id"] = Id(triage), ["timeout_seconds"] = 1 }, null, CancellationToken.None);
        Assert.Contains("still running", Text(notYet));
        var closed = await tool.ExecuteAsync("c8", new JsonObject { ["op"] = "close", ["id"] = Id(triage2) }, null, CancellationToken.None);
        Assert.Contains("closed", Text(closed));

        gate.SetResult();
        var done = await tool.ExecuteAsync("c9", new JsonObject { ["op"] = "wait", ["id"] = Id(worker), ["timeout_seconds"] = 30 }, null, CancellationToken.None);
        Assert.Contains("finished", Text(done));
        var listed = await tool.ExecuteAsync("c10", new JsonObject { ["op"] = "list" }, null, CancellationToken.None);
        Assert.Contains(Id(worker), Text(listed));
    }

    [Fact]
    public void HelperRolesComeFromTheKbsActiveRules_ButNeverReplaceABuiltInOne()
    {
        var roles = TaskTool.ParseRoles([
            "# Rule\n```helper-role\n{\"name\":\"log-reader\",\"instructions\":\"Read the seat log named and say what failed.\",\"writes\":false}\n```\n",
            "```helper-role\n{\"name\":\"worker\",\"instructions\":\"override\"}\n```",
            "```helper-role\nnot json\n```",
        ]);
        Assert.Equal(["log-reader"], roles.Keys);
        Assert.False(roles["log-reader"].Writes);
    }

    /// <summary>plan-pi-sharp-codex item 4: SubagentStart and SubagentStop fire around a helper (Start may add to its task), and
    /// the seat's other hooks run inside it.</summary>
    [Fact]
    public async Task SubagentHooksFireAroundAHelper_AndTheSeatsHooksRunInsideIt()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var ws = new TempWorkspace();
        var log = Path.Combine(ws.Path, "hooks.log");
        var hooks = new CommandHookRunner(new Dictionary<CommandHookEvent, IReadOnlyList<CommandHook>>
        {
            [CommandHookEvent.SubagentStart] = [new($"cat >> '{log}'; echo >> '{log}'; printf '{{\"additional_context\":\"use the Release build\"}}'", "v1", TimeSpan.FromSeconds(5))],
            [CommandHookEvent.SubagentStop] = [new($"cat >> '{log}'; echo >> '{log}'", "v1", TimeSpan.FromSeconds(5))],
            [CommandHookEvent.PostToolUse] = [new($"cat >> '{log}'; echo >> '{log}'", "v1", TimeSpan.FromSeconds(5))],
        });
        var stream = new ScriptedStream()
            .Then(Fakes.ToolCalls(Fakes.Call("h1", "bash", new JsonObject { ["command"] = "echo hi-from-helper" })))
            .Then(Fakes.Text("ran it"));
        var tool = new TaskTool(ws.Path, () => (Sub, stream.Stream), () => "task-1") { Hooks = () => hooks };
        await tool.ExecuteAsync("c", new JsonObject { ["role"] = "triage", ["task"] = "run echo" }, null, CancellationToken.None);
        var fired = File.ReadAllText(log);
        Assert.Contains("\"role\":\"triage\"", fired);              // SubagentStart
        Assert.Contains("hi-from-helper", fired);                    // PostToolUse inside the helper
        Assert.Contains("ran it", fired);                            // SubagentStop is told the summary
        Assert.Contains("[hook] use the Release build", stream.Contexts[0].Messages.OfType<UserMessage>().First().Text);
    }

    [Fact]
    public async Task AWorkerHelperEditsAndReports()
    {
        using var ws = new TempWorkspace();
        var file = ws.File("a.txt", "one\n");
        var stream = new ScriptedStream()
            .Then(Fakes.ToolCalls(Fakes.Call("h1", "apply_patch", new JsonObject { ["input"] = "*** Begin Patch\n*** Add File: b.txt\n+two\n*** End Patch\n" })))
            .Then(Fakes.Text("Added b.txt."));
        var tool = new TaskTool(ws.Path, () => (Sub, stream.Stream), () => "task-1");
        var result = await tool.ExecuteAsync("c", new JsonObject { ["role"] = "worker", ["task"] = "add b.txt with 'two'" }, null, CancellationToken.None);
        Assert.Equal("two\n", File.ReadAllText(Path.Combine(ws.Path, "b.txt")));
        Assert.Contains("Added b.txt.", ((TextContent)result.Content[0]).Text);
    }

    [Fact]
    public async Task TheReviewToolReviewsTheBranchAgainstMain_AndWritesItsFindingsOnTheEpic()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var ws = new TempWorkspace();
        void Git(params string[] args)
        {
            var info = new ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            info.ArgumentList.Add("-C"); info.ArgumentList.Add(ws.Path);
            foreach (var a in new[] { "-c", "user.name=t", "-c", "user.email=t@t" }.Concat(args)) info.ArgumentList.Add(a);
            using var p = Process.Start(info)!; p.WaitForExit();
            Assert.True(p.ExitCode == 0, p.StandardError.ReadToEnd());
        }
        Git("init", "-q");
        ws.File("a.cs", "int x = 1;\n");
        Git("add", "."); Git("commit", "-qm", "base");
        Git("update-ref", "refs/remotes/origin/main", "HEAD");
        ws.File("a.cs", "int x = 2;\n");
        Git("commit", "-qam", "change");
        var stream = new ScriptedStream().Then(Fakes.Text(
            """{"overall_correctness":"incorrect","reason":"x changed without its test","findings":[{"priority":1,"confidence":0.8,"file":"a.cs","start_line":1,"end_line":1,"summary":"x is now 2 and no test covers it"},{"priority":3,"confidence":0.5,"file":"a.cs","start_line":1,"end_line":1,"summary":"name x is vague"}]}"""));
        var notes = new List<(string Entity, string Title, string Body)>();
        var tool = new ReviewTool(ws.Path, () => (Sub, stream.Stream), () => "task-1",
            (entity, title, body, _) => { notes.Add((entity, title, body)); return Task.FromResult(true); });
        var result = await tool.ExecuteAsync("c", new JsonObject { ["epic"] = "hc2000-hdd" }, null, CancellationToken.None);
        var text = ((TextContent)result.Content[0]).Text;
        Assert.Contains("- P1 (0.8) a.cs:1: x is now 2 and no test covers it", text);
        Assert.True(text.IndexOf("P1", StringComparison.Ordinal) < text.IndexOf("P3", StringComparison.Ordinal));
        Assert.Contains("1 finding(s) at P0/P1", text);
        Assert.Contains("(written on hc2000-hdd as a note)", text);
        Assert.Equal("hc2000-hdd", Assert.Single(notes).Entity);
    }
}
