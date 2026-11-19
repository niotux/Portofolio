using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Messages;
using PiSharp.Core.Models;
using PiSharp.Llm.OpenAi;
using PiSharp.Tests.Agent;
using PiSharp.Tests.Tools;

namespace PiSharp.Tests.Llm;

public sealed class RequestBuilderTests
{
    private static readonly ModelConfig TextModel = new()
    {
        Id = "m",
        BaseUrl = "http://localhost/v1",
        MaxOutputTokens = 512,
    };

    private static readonly ModelConfig VisionModel = TextModel with { Input = [ModelInput.Text, ModelInput.Image] };

    private static readonly ImageContent Png = ImageFixtures.Png();

    [Fact]
    public void FullHistory_MatchesWireFormat()
    {
        var tool = new DelegateTool { Name = "read", Description = "Read a file", Parameters = Fakes.Json("""{"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}""") };
        var messages = new List<Message>
        {
            new UserMessage("hi"),
            new AssistantMessage
            {
                Model = "m",
                StopReason = StopReason.ToolUse,
                Content = [new ThinkingContent("let me look", "reasoning_content"), new TextContent("Reading."), Fakes.Call("c1", "read", Fakes.Json("""{"path":"a.txt"}"""))],
            },
            new ToolResultMessage { ToolCallId = "c1", ToolName = "read", Content = [new TextContent("line1"), new TextContent("line2")] },
            new UserMessage("thanks"),
        };

        var body = RequestBuilder.Build(TextModel, new LlmContext("SYS", messages, [tool]), new StreamOptions { Temperature = 0.2 });

        var expected = JsonNode.Parse("""
        {
          "model": "m",
          "messages": [
            {"role":"system","content":"SYS"},
            {"role":"user","content":"hi"},
            {"role":"assistant","content":"Reading.","reasoning_content":"let me look","tool_calls":[{"id":"c1","type":"function","function":{"name":"read","arguments":"{\"path\":\"a.txt\"}"}}]},
            {"role":"tool","content":"line1\nline2","tool_call_id":"c1"},
            {"role":"user","content":"thanks"}
          ],
          "stream": true,
          "stream_options": {"include_usage": true},
          "max_tokens": 512,
          "temperature": 0.2,
          "tools": [{"type":"function","function":{"name":"read","description":"Read a file","parameters":{"type":"object","properties":{"path":{"type":"string"}},"required":["path"]},"strict":false}}]
        }
        """);
        Assert.True(JsonNode.DeepEquals(expected, body), body.ToJsonString());
    }

    [Fact]
    public void UserImages_BecomeDataUrisForVisionModels_AndPlaceholdersOtherwise()
    {
        var user = new UserMessage([new TextContent("look"), Png, Png]);

        var vision = RequestBuilder.Build(VisionModel, new LlmContext("", [user], []), new StreamOptions());
        var content = vision["messages"]![0]!["content"]!.AsArray();
        Assert.Equal(3, content.Count);
        Assert.Equal("data:image/png;base64," + Png.Data, content[1]!["image_url"]!["url"]!.GetValue<string>());

        var text = RequestBuilder.Build(TextModel, new LlmContext("", [user], []), new StreamOptions());
        Assert.Equal("look" + MessageTransformer.ImagePlaceholder, text["messages"]![0]!["content"]!.GetValue<string>());
    }

    [Fact]
    public void ToolResultImages_AreAttachedInAFollowUpUserMessage()
    {
        var messages = new List<Message>
        {
            new UserMessage("go"),
            Fakes.ToolCalls(Fakes.Call("c1", "shot"), Fakes.Call("c2", "shot")) with { Model = "m" },
            new ToolResultMessage { ToolCallId = "c1", ToolName = "shot", Content = [Png] },
            new ToolResultMessage { ToolCallId = "c2", ToolName = "shot", Content = [new TextContent("done"), Png] },
        };

        var body = RequestBuilder.Build(VisionModel, new LlmContext("", messages, []), new StreamOptions());
        var wire = body["messages"]!.AsArray();

        Assert.Equal(["user", "assistant", "tool", "tool", "user"], wire.Select(m => m!["role"]!.GetValue<string>()));
        Assert.Equal("(see attached image)", wire[2]!["content"]!.GetValue<string>());
        Assert.Equal("done", wire[3]!["content"]!.GetValue<string>());
        var attached = wire[4]!["content"]!.AsArray();
        Assert.Equal("Attached image(s) from tool result:", attached[0]!["text"]!.GetValue<string>());
        Assert.Equal(2, attached.Count(p => p!["type"]!.GetValue<string>() == "image_url"));

        // Tool calls in history with no active tools still send an empty tool list.
        Assert.Empty(body["tools"]!.AsArray());
    }

    [Fact]
    public void ThinkingFormats_ProduceTheRightKnobs()
    {
        var context = new LlmContext("", [new UserMessage("q")], []);
        var low = new StreamOptions { Reasoning = ThinkingLevel.Low };
        var off = new StreamOptions { Reasoning = ThinkingLevel.Off };

        var effort = TextModel with { Reasoning = true, Compat = new ModelCompat { ThinkingFormat = ThinkingFormat.ReasoningEffort } };
        Assert.Equal("low", RequestBuilder.Build(effort, context, low)["reasoning_effort"]!.GetValue<string>());
        Assert.Null(RequestBuilder.Build(effort, context, off)["reasoning_effort"]);

        var qwen = TextModel with { Reasoning = true, Compat = new ModelCompat { ThinkingFormat = ThinkingFormat.QwenChatTemplate } };
        Assert.True(RequestBuilder.Build(qwen, context, low)["chat_template_kwargs"]!["enable_thinking"]!.GetValue<bool>());
        Assert.False(RequestBuilder.Build(qwen, context, off)["chat_template_kwargs"]!["enable_thinking"]!.GetValue<bool>());

        var template = TextModel with
        {
            Reasoning = true,
            Compat = new ModelCompat
            {
                ThinkingFormat = ThinkingFormat.ChatTemplate,
                ChatTemplateKwargs = Fakes.Json("""{"thinking":"$thinking.enabled","budget":"$thinking.budget","keep":1}"""),
                ThinkingTokenBudgetField = "thinking_budget_tokens",
            },
        };
        var templated = RequestBuilder.Build(template, context, new StreamOptions { Reasoning = ThinkingLevel.Medium });
        Assert.True(JsonNode.DeepEquals(Fakes.Json("""{"thinking":true,"budget":8192,"keep":1}"""), templated["chat_template_kwargs"]));
        Assert.Equal(8192, templated["thinking_budget_tokens"]!.GetValue<int>());

        var deepseek = TextModel with { Reasoning = true, Compat = new ModelCompat { ThinkingFormat = ThinkingFormat.DeepSeek } };
        Assert.Equal("enabled", RequestBuilder.Build(deepseek, context, low)["thinking"]!["type"]!.GetValue<string>());
        Assert.Equal("disabled", RequestBuilder.Build(deepseek, context, off)["thinking"]!["type"]!.GetValue<string>());

        // A model that cannot reason never gets thinking knobs, whatever the level.
        var plain = TextModel with { Compat = new ModelCompat { ThinkingFormat = ThinkingFormat.ReasoningEffort } };
        Assert.Null(RequestBuilder.Build(plain, context, low)["reasoning_effort"]);
    }

    [Fact]
    public void CompatKnobs_AreHonored()
    {
        var model = TextModel with
        {
            Reasoning = true,
            Compat = new ModelCompat
            {
                MaxTokensField = "max_completion_tokens",
                SupportsUsageInStreaming = false,
                SupportsDeveloperRole = true,
                RequiresToolResultName = true,
                RequiresAssistantAfterToolResult = true,
                RequiresReasoningContentOnAssistantMessages = true,
                SupportsStrictMode = false,
            },
        };
        var messages = new List<Message>
        {
            new UserMessage("go"),
            Fakes.ToolCalls(Fakes.Call("c1", "t")) with { Model = "m" },
            new ToolResultMessage { ToolCallId = "c1", ToolName = "t", Content = [] },
            new UserMessage("next"),
        };
        var body = RequestBuilder.Build(model, new LlmContext("SYS", messages, [new DelegateTool()]), new StreamOptions { MaxTokens = 99 });

        Assert.Null(body["stream_options"]);
        Assert.Null(body["max_tokens"]);
        Assert.Equal(99, body["max_completion_tokens"]!.GetValue<int>());
        var wire = body["messages"]!.AsArray();
        Assert.Equal("developer", wire[0]!["role"]!.GetValue<string>());
        Assert.Equal("", wire[2]!["content"]!.GetValue<string>());
        Assert.Equal("", wire[2]!["reasoning_content"]!.GetValue<string>());
        Assert.Equal("t", wire[3]!["name"]!.GetValue<string>());
        Assert.Equal("(no tool output)", wire[3]!["content"]!.GetValue<string>());
        Assert.Equal("I have processed the tool results.", wire[4]!["content"]!.GetValue<string>());
        Assert.Equal("next", wire[5]!["content"]!.GetValue<string>());
        Assert.Null(body["tools"]![0]!["function"]!["strict"]);
    }

    [Fact]
    public void ThinkingAsText_IsInlinedIntoContentArray()
    {
        var model = TextModel with { Compat = new ModelCompat { RequiresThinkingAsText = true } };
        var assistant = new AssistantMessage { Model = "m", Content = [new ThinkingContent("why"), new TextContent("answer")] };
        var body = RequestBuilder.Build(model, new LlmContext("", [new UserMessage("q"), assistant], []), new StreamOptions());
        var content = body["messages"]![1]!["content"]!.AsArray();
        Assert.Equal(["why", "answer"], content.Select(p => p!["text"]!.GetValue<string>()));
    }
}

public sealed class MessageTransformerTests
{
    private static readonly ModelConfig Model = new() { Id = "m", BaseUrl = "http://localhost/v1" };

    [Fact]
    public void FailedAssistantTurns_AreDropped()
    {
        var messages = new List<Message>
        {
            new UserMessage("q"),
            Fakes.Error("boom") with { Model = "m" },
            Fakes.Assistant(StopReason.Aborted, new TextContent("half")) with { Model = "m" },
            Fakes.Text("ok") with { Model = "m" },
        };
        var result = MessageTransformer.Transform(Model, messages);
        Assert.Equal(["user", "assistant"], result.Select(Fakes.Role));
        Assert.Equal("ok", ((AssistantMessage)result[1]).Text);
    }

    [Fact]
    public void OrphanedToolCalls_GetSynthesizedErrorResults()
    {
        var messages = new List<Message>
        {
            new UserMessage("q"),
            Fakes.ToolCalls(Fakes.Call("c1", "a"), Fakes.Call("c2", "b")) with { Model = "m" },
            new ToolResultMessage { ToolCallId = "c2", ToolName = "b", Content = [new TextContent("done")] },
            new UserMessage("next"),
            Fakes.ToolCalls(Fakes.Call("c3", "a")) with { Model = "m" },
        };
        var result = MessageTransformer.Transform(Model, messages);

        Assert.Equal(["user", "assistant", "toolResult", "toolResult", "user", "assistant", "toolResult"], result.Select(Fakes.Role));
        var synthesized = (ToolResultMessage)result[3];
        Assert.Equal(("c1", "a", true), (synthesized.ToolCallId, synthesized.ToolName, synthesized.IsError));
        Assert.Equal(MessageTransformer.MissingResultText, Fakes.ResultText(synthesized));
        Assert.Equal("c3", ((ToolResultMessage)result[6]).ToolCallId);
    }

    [Fact]
    public void ThinkingFromAnotherModel_BecomesText()
    {
        var foreign = new AssistantMessage { Model = "other", Content = [new ThinkingContent("secret plan"), new TextContent("hi")] };
        var same = new AssistantMessage { Model = "m", Content = [new ThinkingContent("", null), new ThinkingContent("kept", "reasoning_content")] };
        var result = MessageTransformer.Transform(Model, [foreign, same]);

        Assert.Equal([new TextContent("secret plan"), new TextContent("hi")], ((AssistantMessage)result[0]).Content);
        Assert.Equal([new ThinkingContent("kept", "reasoning_content")], ((AssistantMessage)result[1]).Content);
    }

    [Fact]
    public void ConsecutiveImages_CollapseToOnePlaceholder()
    {
        var img = ImageFixtures.Png();
        var result = MessageTransformer.Transform(Model, [new UserMessage([img, img, new TextContent("x"), img])]);
        var content = ((UserMessage)result[0]).Content;
        Assert.Equal([new TextContent(MessageTransformer.ImagePlaceholder), new TextContent("x"), new TextContent(MessageTransformer.ImagePlaceholder)], content);
    }
}
