using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Compaction;
using PiSharp.Core.Config;
using PiSharp.Core.Messages;
using PiSharp.Core.Models;
using PiSharp.Llm.Responses;

namespace PiSharp.Tests.Llm;

// plan-pi-sharp-codex phase 3: prompts and context.
public sealed class CodexContextTests
{
    private static ModelConfig Codex => new() { Id = "gpt-6.1-sol", BaseUrl = "https://broker", Provider = "openai-codex", Connection = "c", ContextWindow = 200000, MaxOutputTokens = 32000, Reasoning = true };

    private static AssistantMessage Assistant(params ContentPart[] parts) =>
        new() { Model = Codex.Id, Provider = Codex.Provider, Content = parts, StopReason = StopReason.Stop };

    [Fact]
    public void TheProviderCompactsBelowTheThreshold_AndRequestsStartAtItsItem()
    {
        var item = """{"type":"compaction","id":"cmp_1","encrypted_content":"opaque"}""";
        var messages = new List<Message>
        {
            new UserMessage("old work"), Assistant(new TextContent("old answer")),
            Assistant(new ThinkingContent("[the provider compacted the context here]", item)),
            new UserMessage("new work"),
        };
        Assert.Equal(2, ServerCompaction.LatestIndex(messages));
        var body = ResponsesRequest.Build(Codex, new LlmContext("sys", messages, []), new StreamOptions { ServerCompactThreshold = 171000 });
        Assert.Equal(171000, body["context_management"]![0]!["compact_threshold"]!.GetValue<int>());
        var input = body["input"]!.AsArray().OfType<JsonObject>().ToList();
        Assert.Equal("compaction", input[0]["type"]!.GetValue<string>()); // everything before it dropped
        Assert.DoesNotContain(input, i => i.ToJsonString().Contains("old work"));
        Assert.Contains(input, i => i.ToJsonString().Contains("new work"));
        Assert.Null(ResponsesRequest.Build(Codex, new LlmContext("sys", messages, []), new StreamOptions())["context_management"]);
    }

    [Fact]
    public void AStreamedCompactionItemIsKeptToSendBack()
    {
        var assembler = new ResponsesAssembler(Codex);
        JsonObject E(string json) => JsonNode.Parse(json)!.AsObject();
        assembler.Push(E("""{"type":"response.output_item.added","output_index":0,"item":{"type":"compaction","id":"cmp_2"}}"""));
        assembler.Push(E("""{"type":"response.output_item.done","output_index":0,"item":{"type":"compaction","id":"cmp_2","encrypted_content":"x"}}"""));
        var part = Assert.Single(assembler.Snapshot().Content);
        Assert.True(ServerCompaction.IsItem(part));
        Assert.Contains("\"encrypted_content\":\"x\"", ((ThinkingContent)part).Signature);
    }

    [Fact]
    public void GptSeatsGetCodexsWorkingStyle_AndInstructionFilesStopAtTheCap()
    {
        var gpt = SystemPrompt.Build(new SystemPromptOptions { Cwd = "/w", ModelFamily = "gpt" });
        Assert.Contains("# How you work (adapted from OpenAI Codex)", gpt);
        Assert.Contains("Keep going until the task is completely resolved", gpt);
        Assert.DoesNotContain("adapted from OpenAI Codex", SystemPrompt.Build(new SystemPromptOptions { Cwd = "/w" }));

        var big = new string('x', 3000);
        var capped = SystemPrompt.Build(new SystemPromptOptions
        {
            Cwd = "/w", ProjectDocMaxBytes = 4000,
            ContextFiles = [new ContextFile("/w/AGENTS.md", big), new ContextFile("/w/sub/AGENTS.md", big), new ContextFile("/w/sub/deep/AGENTS.md", big)],
        });
        Assert.Contains("cut at the 3 KiB instruction limit; read /w/sub/AGENTS.md for the rest", capped);
        Assert.Contains("More instruction files past the 3 KiB limit", capped);
        Assert.Contains("/w/sub/deep/AGENTS.md", capped);
        var context = capped[(capped.IndexOf("<project_context>") + "<project_context>".Length)..capped.IndexOf("</project_context>")];
        Assert.InRange(context.Count(c => c == 'x'), 3000, 4000);
    }

    [Fact]
    public void TheGptTextNamesOnlyTheToolsTheSeatHas_AndStandingTextIsCappedLikeInstructionFiles()
    {
        var all = SystemPrompt.Build(new SystemPromptOptions { Cwd = "/w", ModelFamily = "gpt", SelectedTools = ["read", "bash", "apply_patch", "plan", "task", "review"] });
        Assert.Contains("keep a plan with the plan tool", all);
        Assert.Contains("## Planning", all);
        var bare = SystemPrompt.Build(new SystemPromptOptions { Cwd = "/w", ModelFamily = "gpt", SelectedTools = ["read", "bash", "edit"] });
        Assert.DoesNotContain("plan tool", bare);
        Assert.DoesNotContain("## Planning", bare); // a heading left empty goes too
        Assert.DoesNotContain("task tool", bare);
        Assert.DoesNotContain("review tool", bare);
        Assert.DoesNotContain("`apply_patch`", bare);
        Assert.Contains("## Validating your work", bare);

        Assert.Equal("short", SystemPrompt.CapInstructions("short", 1024, "read the rest"));
        var capped = SystemPrompt.CapInstructions(new string('é', 3000), 2048, "read the rest with kb_get coder-06-pi3");
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(capped[..capped.IndexOf("\n[", StringComparison.Ordinal)]) <= 2048);
        Assert.EndsWith("[... cut at the 2 KiB instruction limit; read the rest with kb_get coder-06-pi3]", capped);
    }
}
