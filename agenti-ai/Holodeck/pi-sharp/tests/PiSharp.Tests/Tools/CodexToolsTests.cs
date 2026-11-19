using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Messages;
using PiSharp.Core.Models;
using PiSharp.Llm.Responses;
using PiSharp.Tests.Agent;
using PiSharp.Tools;

namespace PiSharp.Tests.Tools;

// plan-pi-sharp-codex phase 2: the working tools as GPT models are trained on them (Codex).
public sealed class CodexToolsTests
{
    private static ModelConfig Codex => new() { Id = "gpt-6.1-sol", BaseUrl = "https://broker", Provider = "openai-codex", Connection = "c", ContextWindow = 200000, MaxOutputTokens = 32000, Reasoning = true };
    private static ModelConfig Xai => Codex with { Provider = "xai", Id = "grok" };

    private static IReadOnlySet<string> Free => new HashSet<string> { "apply_patch" };

    [Fact]
    public void ApplyPatchGoesFreeFormToTheCodexBackend_WithItsHistoryAsCustomCalls()
    {
        using var ws = new TempWorkspace();
        var patch = new PatchTool(ws.Path);
        var call = new ToolCallContent("call_1|ctc_1", "apply_patch", new JsonObject { ["input"] = "*** Begin Patch\n*** End Patch\n" });
        var messages = new List<Message>
        {
            new UserMessage("edit it"),
            new AssistantMessage { Model = Codex.Id, Provider = Codex.Provider, Content = [call], StopReason = StopReason.ToolUse },
            new ToolResultMessage { ToolCallId = "call_1|ctc_1", ToolName = "apply_patch", Content = [new TextContent("Success.")] },
        };
        var body = ResponsesRequest.Build(Codex, new LlmContext("sys", messages, [patch]), new StreamOptions { FreeformTools = Free });

        var tool = Assert.Single(body["tools"]!.AsArray())!.AsObject();
        Assert.Equal("custom", tool["type"]!.GetValue<string>());
        Assert.Equal("lark", tool["format"]!["syntax"]!.GetValue<string>());
        Assert.Contains("start: begin_patch hunk+ end_patch", tool["format"]!["definition"]!.GetValue<string>());
        var items = body["input"]!.AsArray().OfType<JsonObject>().ToList();
        var customCall = items.Single(i => i["type"]?.GetValue<string>() == "custom_tool_call");
        Assert.Equal("*** Begin Patch\n*** End Patch\n", customCall["input"]!.GetValue<string>());
        Assert.Equal("ctc_1", customCall["id"]!.GetValue<string>());
        Assert.Single(items, i => i["type"]?.GetValue<string>() == "custom_tool_call_output");

        // Off, or on another provider: a plain function tool as before.
        foreach (var (model, options) in new[] { (Codex, new StreamOptions()), (Xai, new StreamOptions { FreeformTools = Free }) })
        {
            var plain = ResponsesRequest.Build(model, new LlmContext("sys", messages, [patch]), options);
            Assert.Equal("function", plain["tools"]![0]!["type"]!.GetValue<string>());
            Assert.Contains(plain["input"]!.AsArray().OfType<JsonObject>(), i => i["type"]?.GetValue<string>() == "function_call");
        }
    }

    [Fact]
    public void AStreamedCustomCallBecomesAnApplyPatchCallWithItsInput()
    {
        var assembler = new ResponsesAssembler(Codex);
        JsonObject E(string json) => JsonNode.Parse(json)!.AsObject();
        assembler.Push(E("""{"type":"response.output_item.added","output_index":0,"item":{"type":"custom_tool_call","id":"ctc_9","call_id":"call_9","name":"apply_patch","input":""}}"""));
        assembler.Push(E("""{"type":"response.custom_tool_call_input.delta","output_index":0,"delta":"*** Begin Patch\n"}"""));
        Assert.Equal("*** Begin Patch\n", ((ToolCallContent)assembler.Snapshot().Content[0]).Arguments["input"]!.GetValue<string>());
        assembler.Push(E("""{"type":"response.output_item.done","output_index":0,"item":{"type":"custom_tool_call","id":"ctc_9","call_id":"call_9","name":"apply_patch","input":"*** Begin Patch\n*** End Patch\n"}}"""));
        var call = (ToolCallContent)assembler.Snapshot().Content[0];
        Assert.Equal("call_9|ctc_9", call.Id);
        Assert.Equal("apply_patch", call.Name);
        Assert.Equal("*** Begin Patch\n*** End Patch\n", call.Arguments["input"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("apply_patch <<'EOF'\n*** Begin Patch\n*** End Patch\nEOF", true)]
    [InlineData("apply_patch <<EOF\n*** Begin Patch\n*** End Patch\nEOF\n", true)]
    [InlineData("apply_patch <<'PATCH'\n*** Begin Patch\n*** End Patch\nPATCH", true)]
    [InlineData("cat <<'EOF'\n*** Begin Patch\nEOF", false)]
    [InlineData("apply_patch <<'EOF'\nnot a patch\nEOF", false)]
    [InlineData("echo apply_patch", false)]
    [InlineData("apply_patch <<'EOF\n*** Begin Patch\n*** End Patch\nEOF", false)]
    [InlineData("apply_patch <<EOF\n*** Begin Patch\n*** End Patch\nEOF\necho more", false)]
    public void APatchTypedIntoTheShellIsRecognised(string command, bool patch) =>
        Assert.Equal(patch, ShellPatch.Extract(command) is not null);

    /// <summary>A long patch is read whole, with no time limit to run out on a loaded seat.</summary>
    [Fact]
    public void ALongPatchTypedIntoTheShellIsReadWhole()
    {
        var lines = string.Concat(Enumerable.Range(0, 20_000).Select(i => $"+line {i}\n"));
        var body = ShellPatch.Extract($"apply_patch <<'EOF'\r\n*** Begin Patch\n*** Add File: big.txt\n{lines}*** End Patch\n  EOF  \n");
        Assert.NotNull(body);
        Assert.EndsWith("*** End Patch", body);
        Assert.Contains("+line 19999", body);
    }

    [Fact]
    public async Task APatchTypedIntoTheShellRunsAsTheApplyPatchTool()
    {
        using var ws = new TempWorkspace();
        var file = ws.File("a.txt", "one\ntwo\n");
        var bashRan = false;
        var bash = new DelegateTool
        {
            Name = "bash",
            Parameters = JsonNode.Parse("""{"type":"object","properties":{"command":{"type":"string"}},"required":["command"]}""")!.AsObject(),
            Execute = (_, _, _, _) => { bashRan = true; return Task.FromResult(AgentToolResult.FromText("bash")); },
        };
        var ctx = new AgentContext { Tools = [bash, new PatchTool(ws.Path)] };
        var stream = new ScriptedStream()
            .Then(Fakes.ToolCalls(Fakes.Call("c1", "bash", new JsonObject
                { ["command"] = "apply_patch <<'EOF'\n*** Begin Patch\n*** Update File: a.txt\n@@\n one\n-two\n+TWO\n*** End Patch\nEOF" })))
            .Then(Fakes.Text("done"));
        await Fakes.Collect(AgentLoop.Run([new UserMessage("go")], ctx, Fakes.Config(), stream.Stream));
        Assert.False(bashRan);
        Assert.Equal("one\nTWO\n", File.ReadAllText(file));
        var result = ctx.Messages.OfType<ToolResultMessage>().Single();
        Assert.Equal("bash", result.ToolName); // the result answers the call the model made
        Assert.StartsWith("Success.", Fakes.ResultText(result));
    }

    [Fact]
    public async Task APatchMatchesIgnoringSurroundingSpacesThenTypographicPunctuation()
    {
        using var ws = new TempWorkspace();
        var file = ws.File("a.txt", "  title — “quoted” text\nkeep\n");
        var result = await new PatchTool(ws.Path).ExecuteAsync("t", new JsonObject
        {
            ["input"] = "*** Begin Patch\n*** Update File: a.txt\n@@\n-title - \"quoted\" text\n+new title\n keep\n*** End Patch\n",
        }, null, CancellationToken.None);
        Assert.False(result.IsError);
        Assert.Equal("new title\nkeep\n", File.ReadAllText(file));
    }

    [Fact]
    public async Task ATtyCommandRunsInATerminal_AndEveryCommandGetsTheFixedEnvironment()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists(BashTool.ScriptPath)) return;
        using var ws = new TempWorkspace();
        var tty = await ToolRun.Exec(new BashTool(ws.Path), """{"command":"if [ -t 1 ]; then echo terminal; else echo pipe; fi","tty":true}""");
        Assert.Contains("terminal", tty.Text);
        var plain = await ToolRun.Exec(new BashTool(ws.Path), """{"command":"if [ -t 1 ]; then echo terminal; else echo pipe; fi; echo $NO_COLOR $PAGER $GIT_TERMINAL_PROMPT $LC_ALL; printf 'caf\\303\\251' | wc -m"}""");
        Assert.Contains("pipe", plain.Text);
        Assert.Contains("1 cat 0 C.UTF-8", plain.Text);
        Assert.Contains("4", plain.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Last()); // "café" is 4 characters in UTF-8, 5 bytes
    }
}
