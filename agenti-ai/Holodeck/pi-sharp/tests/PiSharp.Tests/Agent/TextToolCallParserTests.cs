using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Messages;
using static PiSharp.Tests.Agent.Fakes;

namespace PiSharp.Tests.Agent;

public sealed class TextToolCallParserTests
{
    private static readonly DelegateTool Read = new() { Name = "read" };

    [Fact]
    public void RecoversHermesJsonInsideToolCallTags()
    {
        var text = """
        I'll look.
        <tool_call>
        {"name":"read","arguments":{"path":"a.txt"}}
        </tool_call>
        """;
        var calls = TextToolCallParser.Parse(text, knownNames: ["read"]);
        var call = Assert.Single(calls);
        Assert.Equal("read", call.Name);
        Assert.Equal("a.txt", call.Arguments["path"]!.GetValue<string>());
    }

    [Fact]
    public void RecoversQwenFunctionParameterTags()
    {
        var text = """
        <tool_call>
        <function=read>
        <parameter=path>src/a.cs</parameter>
        <parameter=offset>4</parameter>
        </function>
        </tool_call>
        """;
        var call = Assert.Single(TextToolCallParser.Parse(text, ["read"]));
        Assert.Equal("read", call.Name);
        Assert.Equal("src/a.cs", call.Arguments["path"]!.GetValue<string>());
        Assert.Equal(4, call.Arguments["offset"]!.GetValue<int>());
    }

    [Fact]
    public void RecoversNameThenJsonInsideToolCallTags()
    {
        var text = """
        <tool_call>
        read
        {"path":"b.txt"}
        </tool_call>
        """;
        var call = Assert.Single(TextToolCallParser.Parse(text, ["read"]));
        Assert.Equal("read", call.Name);
        Assert.Equal("b.txt", call.Arguments["path"]!.GetValue<string>());
    }

    [Fact]
    public void WholeMessageJson_OnlyWhenNameIsAKnownTool()
    {
        var json = """{"name":"read","parameters":{"path":"c.txt"}}""";
        var hit = Assert.Single(TextToolCallParser.Parse(json, ["read"]));
        Assert.Equal("c.txt", hit.Arguments["path"]!.GetValue<string>());

        Assert.Empty(TextToolCallParser.Parse("""{"name":"not_a_tool","arguments":{}}""", ["read"]));
        Assert.Empty(TextToolCallParser.Parse("The name of the file is read.json and nothing else.", ["read"]));
    }

    [Fact]
    public void TryRepair_TurnsTextIntoToolUseAndKeepsThinking()
    {
        var original = new AssistantMessage
        {
            Model = "m",
            StopReason = StopReason.Stop,
            Content =
            [
                new ThinkingContent("plan"),
                new TextContent("""<tool_call>{"name":"read","arguments":{"path":"z"}}</tool_call>"""),
            ],
        };
        var repaired = TextToolCallParser.TryRepair(original, [Read]);
        Assert.NotNull(repaired);
        Assert.Equal(StopReason.ToolUse, repaired!.StopReason);
        Assert.Equal("plan", Assert.Single(repaired.Content.OfType<ThinkingContent>()).Thinking);
        Assert.Equal("read", Assert.Single(repaired.ToolCalls).Name);
        Assert.DoesNotContain(repaired.Content, p => p is TextContent { Text: var t } && t.Contains("tool_call"));
    }

    [Fact]
    public void TryRepair_LeavesNativeToolCallsAlone()
    {
        var original = ToolCalls(Call("c1", "read"));
        Assert.Null(TextToolCallParser.TryRepair(original, [Read]));
    }
}
