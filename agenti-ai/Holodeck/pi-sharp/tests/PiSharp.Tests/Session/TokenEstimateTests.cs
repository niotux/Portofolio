using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Messages;
using PiSharp.Core.Session;
using PiSharp.Tests.Agent;

namespace PiSharp.Tests.Session;

public class TokenEstimateTests
{
    [Fact]
    public void EstimateTextTokens_CeilsCharsOverFour()
    {
        Assert.Equal(1, TokenEstimate.EstimateTextTokens("abcd"));
        Assert.Equal(2, TokenEstimate.EstimateTextTokens("abcde"));
        Assert.Equal(0, TokenEstimate.EstimateTextTokens(""));
    }

    [Fact]
    public void EstimateMessageTokens_CountsImagesAs4800Chars()
    {
        var user = new UserMessage([new ImageContent("abc", "image/png")]);
        Assert.Equal(1200, TokenEstimate.EstimateMessageTokens(user));
    }

    [Fact]
    public void EstimateMessageTokens_CountsToolCallNameAndArguments()
    {
        var args = new JsonObject { ["path"] = "a.txt" };
        var assistant = new AssistantMessage
        {
            Content = [new ToolCallContent("1", "read", args)],
            Model = "m",
        };
        var expected = (int)Math.Ceiling(("read".Length + args.ToJsonString().Length) / 4.0);
        Assert.Equal(expected, TokenEstimate.EstimateMessageTokens(assistant));
    }

    [Fact]
    public void EstimateContextTokens_UsesLastAssistantUsagePlusTrailing()
    {
        var t0 = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var assistant = new AssistantMessage
        {
            Content = [new TextContent("hi")],
            Model = "m",
            StopReason = StopReason.Stop,
            Usage = new Usage(Input: 100, Output: 20, TotalTokens: 120),
            Timestamp = t0,
        };
        var trailing = new UserMessage("next") { Timestamp = t0.AddSeconds(1) };
        var estimate = TokenEstimate.EstimateContextTokens([assistant, trailing]);
        Assert.Equal(120, estimate.UsageTokens);
        Assert.Equal(1, estimate.TrailingTokens); // "next" = 4 chars -> 1 token
        Assert.Equal(121, estimate.Tokens);
        Assert.Equal(0, estimate.LastUsageIndex);
    }

    [Fact]
    public void EstimateContextTokens_IgnoresUsageWhenNewerPrefixInserted()
    {
        var t0 = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var assistant = new AssistantMessage
        {
            Content = [new TextContent("old")],
            Model = "m",
            StopReason = StopReason.Stop,
            Usage = new Usage(TotalTokens: 999),
            Timestamp = t0,
        };
        // Rebuilt context puts the later-timestamp summary *before* the kept assistant.
        var summary = new CompactionSummaryMessage("summary text here", 50) { Timestamp = t0.AddMinutes(1) };
        var estimate = TokenEstimate.EstimateContextTokens([summary, assistant]);
        Assert.Null(estimate.LastUsageIndex);
        Assert.Equal(
            TokenEstimate.EstimateMessageTokens(assistant) + TokenEstimate.EstimateMessageTokens(summary),
            estimate.Tokens);
    }

    [Fact]
    public void EstimateContextTokens_AddsSystemPromptAndToolsWhenNoUsage()
    {
        var tool = new DelegateTool();
        var ctx = new LlmContext("sys!", [new UserMessage("hi")], [tool]);
        var estimate = TokenEstimate.EstimateContextTokens(ctx);
        Assert.True(estimate.Tokens > TokenEstimate.EstimateMessageTokens(new UserMessage("hi")));
    }
}
