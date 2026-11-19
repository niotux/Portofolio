// Offline replay of the exact chunk shapes captured from llama.cpp (Qwen3 model, 2026-09-13).
using System.Text.Json.Nodes;
using PiSharp.Core.Messages;
using PiSharp.Core.Models;
using PiSharp.Llm.OpenAi;

namespace PiSharp.Tests.Llm;

public sealed class ChunkAssemblerTests
{
    private static readonly ModelConfig Model = new() { Id = "maxiclaude", BaseUrl = "http://localhost/v1", Reasoning = true };

    private static string Chunk(string delta, string? finish = null, string id = "chatcmpl-1") =>
        $$$"""{"choices":[{"finish_reason":{{{(finish is null ? "null" : $"\"{finish}\"")}}},"index":0,"delta":{{{delta}}}}],"created":1789283000,"id":"{{{id}}}","model":"maxiclaude","system_fingerprint":"b1-629b505","object":"chat.completion.chunk"}""";

    private const string UsageChunk = """{"choices":[],"created":1789283000,"id":"chatcmpl-1","model":"maxiclaude","system_fingerprint":"b1-629b505","object":"chat.completion.chunk","usage":{"completion_tokens":31,"prompt_tokens":44,"total_tokens":75,"prompt_tokens_details":{"cached_tokens":30}},"timings":{"cache_n":30,"prompt_n":14}}""";

    private static (List<AssistantMessageEvent> Events, AssistantMessage Message) Replay(params string[] chunks)
    {
        var assembler = new ChunkAssembler(Model);
        var events = new List<AssistantMessageEvent> { new AssistantStart(assembler.Snapshot()) };
        foreach (var chunk in chunks) events.AddRange(assembler.Apply(chunk));
        events.AddRange(assembler.Finish());
        return (events, ((AssistantDone)events[^1]).Message);
    }

    [Fact]
    public void ReasoningThenText_ProducesThinkingAndTextBlocks()
    {
        var (events, message) = Replay(
            Chunk("""{"role":"assistant","content":null}"""),
            Chunk("""{"reasoning_content":"The user "}"""),
            Chunk("""{"reasoning_content":"wants PONG."}"""),
            Chunk("""{"content":"PO"}"""),
            Chunk("""{"content":"NG"}"""),
            Chunk("{}", finish: "stop"),
            UsageChunk);

        Assert.Equal(StopReason.Stop, message.StopReason);
        Assert.Equal("stop", message.RawStopReason);
        Assert.Equal("chatcmpl-1", message.ResponseId);
        Assert.Null(message.ResponseModel);
        Assert.Collection(message.Content,
            p => Assert.Equal(new ThinkingContent("The user wants PONG.", "reasoning_content"), p),
            p => Assert.Equal(new TextContent("PONG"), p));

        Assert.Equal(new Usage(Input: 14, Output: 31, CacheRead: 30, TotalTokens: 75), message.Usage);

        var kinds = events.Select(e => e.GetType().Name).ToList();
        Assert.Equal(
            ["AssistantStart", "ThinkingStart", "ThinkingDelta", "ThinkingDelta", "TextStart", "TextDelta", "TextDelta", "ThinkingEnd", "TextEnd", "AssistantDone"],
            kinds);
        Assert.Equal(0, Assert.Single(events.OfType<ThinkingEnd>()).ContentIndex);
        Assert.Equal(1, Assert.Single(events.OfType<TextEnd>()).ContentIndex);
    }

    [Fact]
    public void ToolCall_FragmentsAreMergedByIndex()
    {
        var (events, message) = Replay(
            Chunk("""{"role":"assistant","content":null}"""),
            Chunk("""{"tool_calls":[{"index":0,"id":"call_abc","type":"function","function":{"name":"get_weather","arguments":""}}]}"""),
            Chunk("""{"tool_calls":[{"index":0,"function":{"arguments":"{\"ci"}}]}"""),
            Chunk("""{"tool_calls":[{"index":0,"function":{"arguments":"ty\": \"Par"}}]}"""),
            Chunk("""{"tool_calls":[{"index":0,"function":{"arguments":"is\"}"}}]}"""),
            Chunk("{}", finish: "tool_calls"),
            UsageChunk);

        Assert.Equal(StopReason.ToolUse, message.StopReason);
        var call = Assert.Single(message.ToolCalls);
        Assert.Equal("call_abc", call.Id);
        Assert.Equal("get_weather", call.Name);
        Assert.Equal("Paris", call.Arguments["city"]!.GetValue<string>());

        Assert.Single(events.OfType<ToolCallStart>());
        var deltas = events.OfType<ToolCallDelta>().ToList();
        Assert.Equal(4, deltas.Count);
        Assert.Equal("", deltas[0].Delta);
        // Partial arguments are parsed leniently while streaming.
        Assert.Equal("Par", deltas[2].Partial.ToolCalls.Single().Arguments["city"]!.GetValue<string>());
        Assert.Equal(call, Assert.Single(events.OfType<ToolCallEnd>()).ToolCall);
    }

    [Fact]
    public void ParallelToolCalls_UseSeparateBlocks()
    {
        var (_, message) = Replay(
            Chunk("""{"tool_calls":[{"index":0,"id":"a","type":"function","function":{"name":"read","arguments":"{\"path\":\"x\"}"}}]}"""),
            Chunk("""{"tool_calls":[{"index":1,"id":"b","type":"function","function":{"name":"read","arguments":"{\"path\":"}}]}"""),
            Chunk("""{"tool_calls":[{"index":1,"function":{"arguments":"\"y\"}"}}]}"""),
            Chunk("{}", finish: "tool_calls"));

        var calls = message.ToolCalls.ToList();
        Assert.Equal(2, calls.Count);
        Assert.Equal(("a", "x"), (calls[0].Id, calls[0].Arguments["path"]!.GetValue<string>()));
        Assert.Equal(("b", "y"), (calls[1].Id, calls[1].Arguments["path"]!.GetValue<string>()));
    }

    [Fact]
    public void LengthFinish_MapsToLength()
    {
        var (_, message) = Replay(Chunk("""{"content":"abc"}"""), Chunk("{}", finish: "length"));
        Assert.Equal(StopReason.Length, message.StopReason);
        Assert.Equal("length", message.RawStopReason);
    }

    [Fact]
    public void UnknownFinishReason_Throws()
    {
        var assembler = new ChunkAssembler(Model);
        assembler.Apply(Chunk("""{"content":"abc"}""", finish: "content_filter"));
        var ex = Assert.Throws<LlmProtocolException>(() => assembler.Finish());
        Assert.Equal("Provider finish_reason: content_filter", ex.Message);
    }

    [Fact]
    public void MissingFinishReason_Throws()
    {
        var assembler = new ChunkAssembler(Model);
        assembler.Apply(Chunk("""{"content":"abc"}"""));
        var ex = Assert.Throws<LlmProtocolException>(() => assembler.Finish());
        Assert.Equal("Stream ended without finish_reason", ex.Message);
    }

    [Fact]
    public void MissingFinishReason_IsInferredWhenServerNeverSendsOne()
    {
        var model = Model with { Compat = new ModelCompat { SupportsFinishReason = false } };
        var assembler = new ChunkAssembler(model);
        assembler.Apply(Chunk("""{"tool_calls":[{"index":0,"id":"a","function":{"name":"t","arguments":"{}"}}]}"""));
        var done = Assert.IsType<AssistantDone>(assembler.Finish()[^1]);
        Assert.Equal(StopReason.ToolUse, done.Reason);
    }

    [Fact]
    public void ErrorPayload_Throws()
    {
        var assembler = new ChunkAssembler(Model);
        var ex = Assert.Throws<LlmProtocolException>(() =>
            assembler.Apply("""{"error":{"code":400,"message":"the request exceeds the available context size, try increasing it","type":"server_error"}}"""));
        Assert.Contains("exceeds the available context size", ex.Message);
    }

    [Fact]
    public void ResponseModel_IsRecordedWhenDifferent()
    {
        var (_, message) = Replay(
            Chunk("""{"content":"x"}""").Replace("\"model\":\"maxiclaude\"", "\"model\":\"qwen3-27b-q4\""),
            Chunk("{}", finish: "stop"));
        Assert.Equal("qwen3-27b-q4", message.ResponseModel);
    }

    [Fact]
    public void Usage_ParsesCacheAndReasoningDetails()
    {
        var usage = ChunkAssembler.ParseUsage(JsonNode.Parse("""{"prompt_tokens":100,"completion_tokens":40,"prompt_tokens_details":{"cached_tokens":60,"cache_write_tokens":10},"completion_tokens_details":{"reasoning_tokens":25}}""")!.AsObject());
        Assert.Equal(new Usage(Input: 30, Output: 40, CacheRead: 60, CacheWrite: 10, TotalTokens: 140, Reasoning: 25), usage);

        var deepseek = ChunkAssembler.ParseUsage(JsonNode.Parse("""{"prompt_tokens":100,"completion_tokens":1,"prompt_cache_hit_tokens":90}""")!.AsObject());
        Assert.Equal(new Usage(Input: 10, Output: 1, CacheRead: 90, TotalTokens: 101), deepseek);
    }

    [Fact]
    public void Fail_ReturnsErrorWithAccumulatedContent()
    {
        var assembler = new ChunkAssembler(Model);
        assembler.Apply(Chunk("""{"content":"partial"}"""));
        var error = assembler.Fail(StopReason.Aborted, "Request was aborted");
        Assert.Equal(StopReason.Aborted, error.Reason);
        Assert.Equal("partial", error.Error.Text);
        Assert.Equal("Request was aborted", error.Error.ErrorMessage);
    }
}
