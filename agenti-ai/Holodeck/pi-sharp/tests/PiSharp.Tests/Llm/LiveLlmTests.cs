// Integration tests against the real llama.cpp server (see LiveLlm.cs for the endpoint).
using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Messages;
using PiSharp.Core.Models;
using PiSharp.Llm.OpenAi;
using PiSharp.Tests.Agent;
using Xunit.Abstractions;

namespace PiSharp.Tests.Llm;

[Collection("live-llm")]
public sealed class LiveLlmTests(ITestOutputHelper output)
{
    private readonly OpenAiCompletionsClient _client = new();

    [LiveLlmFact]
    public async Task PlainText_StreamsTextAndUsage()
    {
        using var cts = LiveLlm.Deadline();
        var events = await LiveLlm.Collect(_client.Stream(
            LiveLlm.Model(), LiveLlm.Context("Reply with exactly the word PONG and nothing else."), LiveLlm.NoThinking, cts.Token));

        Assert.IsType<AssistantStart>(events[0]);
        var done = Assert.IsType<AssistantDone>(events[^1]);
        Log(done.Message);

        Assert.Equal(StopReason.Stop, done.Reason);
        Assert.Equal("stop", done.Message.RawStopReason);
        Assert.Contains("PONG", done.Message.Text);
        Assert.DoesNotContain(done.Message.Content, p => p is ThinkingContent);

        Assert.Contains(events, e => e is TextStart);
        Assert.Contains(events, e => e is TextDelta);
        var textEnd = Assert.Single(events.OfType<TextEnd>());
        Assert.Equal(done.Message.Text, textEnd.Content);
        Assert.Equal(done.Message.Text, string.Concat(events.OfType<TextDelta>().Select(d => d.Delta)));

        Assert.False(string.IsNullOrEmpty(done.Message.ResponseId));
        Assert.Equal(LiveLlm.ModelId, done.Message.Model);

        var usage = done.Message.Usage;
        Assert.True(usage.Output > 0, "completion tokens");
        Assert.True(usage.Input + usage.CacheRead > 0, "prompt tokens");
        Assert.Equal(usage.Input + usage.Output + usage.CacheRead + usage.CacheWrite, usage.TotalTokens);
    }

    [LiveLlmFact]
    public async Task Reasoning_IsCapturedAsThinkingContent()
    {
        using var cts = LiveLlm.Deadline();
        var options = new StreamOptions { Reasoning = ThinkingLevel.Low, Temperature = 0 };
        var events = await LiveLlm.Collect(_client.Stream(
            LiveLlm.Model(), LiveLlm.Context("What is 2 + 2? Answer with just the number."), options, cts.Token));

        var done = Assert.IsType<AssistantDone>(events[^1]);
        Log(done.Message);

        var thinking = Assert.Single(done.Message.Content.OfType<ThinkingContent>());
        Assert.Equal("reasoning_content", thinking.Signature);
        Assert.False(string.IsNullOrWhiteSpace(thinking.Thinking));
        Assert.Contains("4", done.Message.Text);

        var thinkingStart = events.FindIndex(e => e is ThinkingStart);
        var textStart = events.FindIndex(e => e is TextStart);
        Assert.True(thinkingStart >= 0 && textStart > thinkingStart, "thinking streams before the answer");
        Assert.Equal(thinking.Thinking, Assert.Single(events.OfType<ThinkingEnd>()).Content);
    }

    [LiveLlmFact]
    public async Task ToolCall_IsAssembledFromDeltas()
    {
        var weather = new DelegateTool
        {
            Name = "get_weather",
            Description = "Get the current weather for a city.",
            Parameters = Fakes.Json("""{"type":"object","properties":{"city":{"type":"string","description":"City name"}},"required":["city"]}"""),
        };

        using var cts = LiveLlm.Deadline();
        var events = await LiveLlm.Collect(_client.Stream(
            LiveLlm.Model(),
            LiveLlm.Context("What is the weather in Paris right now? You must call the get_weather tool.", weather),
            LiveLlm.NoThinking,
            cts.Token));

        var done = Assert.IsType<AssistantDone>(events[^1]);
        Log(done.Message);

        Assert.Equal(StopReason.ToolUse, done.Reason);
        Assert.Equal("tool_calls", done.Message.RawStopReason);

        var call = Assert.Single(done.Message.ToolCalls);
        Assert.Equal("get_weather", call.Name);
        Assert.False(string.IsNullOrEmpty(call.Id));
        var city = call.Arguments["city"]!.GetValue<string>();
        Assert.Contains("paris", city, StringComparison.OrdinalIgnoreCase);

        Assert.Single(events.OfType<ToolCallStart>());
        Assert.Contains(events.OfType<ToolCallDelta>(), d => d.Delta.Length > 0);
        var end = Assert.Single(events.OfType<ToolCallEnd>());
        Assert.Equal(call, end.ToolCall);
        Assert.True(JsonNode.DeepEquals(call.Arguments, JsonNode.Parse(string.Concat(events.OfType<ToolCallDelta>().Select(d => d.Delta)))));
    }

    // The probe of the real agent loop. Deterministic in what it checks, not in how the served
    // model behaves: at temperature 0 it still answers differently from run to run, and ~1 in 4
    // repeats the identical add call instead of answering in words (board 98855; reproduced
    // 1-in-4 on coder-06-pi3, 2026-09-20). The loop's repeat guard then does the right thing —
    // the repeat is refused, never executed, the run ends on the guard — but a gate that depends
    // on the model never misbehaving is not a gate: it fails ~25% of clean builds and masks real
    // regressions. The probe is therefore retried from a fresh state (xunit v2 has no per-test
    // retry hook). The body keeps the outcome of every attempt instead of asserting on a single
    // roll: the run passes when any attempt completed the round trip cleanly; only when all
    // attempts fail is the failure reported, with what each attempt saw. A real regression fails
    // every attempt just as deterministically as a good run passes one.
    [LiveLlmFact]
    public async Task AgentLoop_RoundTripsARealToolCall()
    {
        var remarks = new List<string>();
        for (var attempt = 1; attempt <= MaxProbeAttempts; attempt++)
        {
            var (passed, detail) = await TryRoundTripOnce();
            if (passed)
            {
                if (attempt > 1)
                {
                    output.WriteLine($"Round trip clean on attempt {attempt}/{MaxProbeAttempts}; " +
                                     $"earlier attempts: {string.Join("; ", remarks)}");
                }
                return;
            }
            remarks.Add($"attempt {attempt}: {detail}");
        }

        Assert.Fail($"The agent loop never completed the round trip in {MaxProbeAttempts} attempts. " +
                    $"{string.Join("; ", remarks)}");
    }

    // How often the round trip may be attempted. Chosen from the measured flake: 1-in-4 fails,
    // so three independent attempts miss with p ~ 0.016 while a deterministic regression still
    // fails every one of them. Override with PI_SHARP_TEST_LLM_PROBE_ATTEMPTS to run the probe
    // more often when measuring the flake rate itself.
    private static readonly int MaxProbeAttempts = AttemptsFromEnv(3);

    private static int AttemptsFromEnv(int fallback)
    {
        var raw = Environment.GetEnvironmentVariable("PI_SHARP_TEST_LLM_PROBE_ATTEMPTS")?.Trim();
        return int.TryParse(raw, out var n) && n >= 1 ? n : fallback;
    }

    // One attempt of the round trip, from a fresh context and tool instance. Returns instead of
    // throwing so the caller can try again; the detail is the first thing that went wrong.
    private async Task<(bool Passed, string Detail)> TryRoundTripOnce()
    {
        var calls = new List<JsonObject>();
        var add = new DelegateTool
        {
            Name = "add",
            Description = "Add two integers and return the sum.",
            Parameters = Fakes.Json("""{"type":"object","properties":{"a":{"type":"integer"},"b":{"type":"integer"}},"required":["a","b"]}"""),
            Execute = (_, args, _, _) =>
            {
                calls.Add(args);
                var sum = args["a"]!.GetValue<long>() + args["b"]!.GetValue<long>();
                return Task.FromResult(AgentToolResult.FromText(sum.ToString()));
            },
        };

        var context = new AgentContext
        {
            SystemPrompt = "You are a calculator assistant in an automated test. Always use the add tool for additions.",
            Tools = [add],
        };
        var config = new AgentLoopConfig { Model = LiveLlm.Model(), Reasoning = ThinkingLevel.Off, Temperature = 0 };

        using var cts = LiveLlm.Deadline();
        var events = await Fakes.Collect(AgentLoop.Run(
            [new UserMessage("Use the add tool to compute 17 + 25, then reply with only the resulting number.")],
            context, config, _client.Stream, cts.Token));

        foreach (var m in context.Messages.OfType<AssistantMessage>())
        {
            Log(m);
            // A run cut short by the per-request deadline is a transport flake, not an outcome:
            // the shared server queues behind other tenants, so retry with a fresh context.
            if (m.StopReason is StopReason.Aborted or StopReason.Error)
            {
                return (false, $"stream ended on {m.StopReason}: {Truncate(m.ErrorMessage ?? "")}");
            }
        }

        var ends = events.OfType<ToolExecutionEnd>().ToList();
        if (ends.Count != 1) return (false, DescribeRepeat(ends, calls));
        var toolEnd = ends[0];
        if (toolEnd.ToolName != "add" || toolEnd.IsError || toolEnd.Result.Text != "42" || calls.Count != 1)
        {
            return (false, $"tool execution wrong: {ends.Count} end(s), error={toolEnd.IsError} " +
                           $"'{toolEnd.Result.Text}', calls={calls.Count}");
        }
        if (events[^1] is not AgentEnd) return (false, $"run ended on {events[^1].GetType().Name}, not AgentEnd");
        if (events.Count(e => e is TurnStart) != 2) return (false, DescribeTurns(events));
        var toolResults = context.Messages.OfType<ToolResultMessage>().ToList();
        if (toolResults.Count != 1) return (false, $"{toolResults.Count} tool results in context, expected 1");

        var final = context.Messages.OfType<AssistantMessage>().Last();
        if (final.StopReason != StopReason.Stop) return (false, $"final stop reason {final.StopReason}");
        if (!final.Text.Contains("42")) return (false, $"final answer '{Truncate(final.Text)}' has no 42");
        return (true, "");
    }

    // The known flake: the model repeats the identical add call, the loop's repeat guard refuses
    // it, and the run ends on the guard instead of on an answer. Named here so the failure says
    // which of the two it was, instead of a bare "expected 1 ToolExecutionEnd, got 2".
    private static string DescribeRepeat(List<ToolExecutionEnd> ends, List<JsonObject> calls)
    {
        if (ends.Count > 1)
        {
            return ends[1].IsError
                ? "the model repeated the identical tool call and the repeat guard ended the run " +
                  "before it answered (the known ~1-in-4 flake, board 98855)"
                : $"the tool executed {ends.Count} times";
        }
        if (ends.Count == 0) return "no tool call at all";
        return calls.Count > 1 ? $"tool executed {calls.Count} times" : ends[0].Result.Text;
    }

    private static string DescribeTurns(List<AgentEvent> events) =>
        $"expected 2 turns, got {events.Count(e => e is TurnStart)} " +
        $"({string.Join(",", events.Select(e => e.GetType().Name).Distinct())})";

    [LiveLlmFact]
    public async Task Image_IsUnderstoodByTheVisionModel()
    {
        var image = LiveLlm.SolidPng(64, 64, 220, 20, 20);
        var user = new UserMessage([
            new TextContent("What is the dominant color of this image? Answer with a single lowercase color word."),
            image,
        ]);
        var context = new LlmContext("You are a terse assistant used in an automated test.", [user], []);

        using var cts = LiveLlm.Deadline();
        var events = await LiveLlm.Collect(_client.Stream(LiveLlm.Model(), context, LiveLlm.NoThinking, cts.Token));

        var done = Assert.IsType<AssistantDone>(events[^1]);
        Log(done.Message);
        Assert.Equal(StopReason.Stop, done.Reason);
        Assert.Contains("red", done.Message.Text, StringComparison.OrdinalIgnoreCase);
    }

    [LiveLlmFact]
    public async Task InvalidApiKey_ProducesErrorStop()
    {
        using var cts = LiveLlm.Deadline();
        List<AssistantMessageEvent> events;
        try
        {
            events = await LiveLlm.Collect(_client.Stream(
                LiveLlm.Model(apiKey: "definitely-wrong"), LiveLlm.Context("Hello"), LiveLlm.NoThinking, cts.Token));
        }
        catch (LlmHttpException ex)
        {
            // Stream() currently throws HTTP 4xx instead of yielding AssistantError (never-throws
            // hole at SendWithRetryAsync). The 401 assertion still runs on the exception.
            output.WriteLine(ex.Message);
            LiveLlm.SkipIfSharedRouterGenericError(ex.Message, "401", output);
            Assert.Equal(401, ex.StatusCode);
            Assert.Contains("401", ex.Message);
            return;
        }

        output.WriteLine(LiveLlm.LastErrorText(events) ?? "(no error text)");
        LiveLlm.SkipIfSharedRouterGenericError(LiveLlm.LastErrorText(events), "401", output);
        Assert.IsType<AssistantStart>(events[0]);
        var error = Assert.IsType<AssistantError>(events[^1]);
        Assert.Equal(StopReason.Error, error.Reason);
        Assert.Equal(StopReason.Error, error.Error.StopReason);
        Assert.Contains("401", error.Error.ErrorMessage);
        Assert.Equal(2, events.Count);
    }

    [LiveLlmFact]
    public async Task Cancellation_MidStream_ProducesAbortedStop()
    {
        using var cts = LiveLlm.Deadline();
        var events = new List<AssistantMessageEvent>();
        await foreach (var e in _client.Stream(
            LiveLlm.Model(),
            LiveLlm.Context("Count from 1 to 200, one number per line."),
            LiveLlm.NoThinking with { MaxTokens = 1000 },
            cts.Token))
        {
            events.Add(e);
            if (e is TextDelta && !cts.IsCancellationRequested) cts.Cancel();
        }

        var error = Assert.IsType<AssistantError>(events[^1]);
        Assert.Equal(StopReason.Aborted, error.Reason);
        Assert.Equal("Request was aborted", error.Error.ErrorMessage);
        Assert.DoesNotContain(events, e => e is AssistantDone);
        Assert.NotEmpty(error.Error.Text);
    }

    [LiveLlmFact]
    public async Task ContextOverflow_IsDetected()
    {
        // n_ctx is ~200k; a prompt several times that size must be rejected by llama.cpp.
        var huge = string.Concat(Enumerable.Repeat("lorem ipsum dolor sit amet ", 400_000));
        using var cts = LiveLlm.Deadline();
        List<AssistantMessageEvent> events;
        try
        {
            events = await LiveLlm.Collect(_client.Stream(
                LiveLlm.Model(), LiveLlm.Context(huge), LiveLlm.NoThinking, cts.Token));
        }
        catch (LlmHttpException ex)
        {
            output.WriteLine(ex.Message);
            LiveLlm.SkipIfSharedRouterGenericError(ex.Message, "context-overflow", output);
            Assert.True(PiSharp.Llm.OverflowDetector.IsOverflowErrorMessage(ex.Message), ex.Message);
            return;
        }

        output.WriteLine(LiveLlm.LastErrorText(events) ?? "(no error text)");
        LiveLlm.SkipIfSharedRouterGenericError(LiveLlm.LastErrorText(events), "context-overflow", output);
        var error = Assert.IsType<AssistantError>(events[^1]);
        Assert.Equal(StopReason.Error, error.Reason);
        Assert.True(PiSharp.Llm.OverflowDetector.IsContextOverflow(error.Error, LiveLlm.Model().ContextWindow), error.Error.ErrorMessage);
    }

    private void Log(AssistantMessage message)
    {
        output.WriteLine($"[{message.StopReason}/{message.RawStopReason}] usage={message.Usage}");
        foreach (var part in message.Content)
        {
            output.WriteLine(part switch
            {
                TextContent t => $"  text: {t.Text}",
                ThinkingContent th => $"  thinking({th.Signature}): {Truncate(th.Thinking)}",
                ToolCallContent c => $"  toolCall {c.Id} {c.Name} {c.Arguments.ToJsonString()}",
                _ => $"  {part}",
            });
        }
    }

    private static string Truncate(string s) => s.Length <= 300 ? s : s[..300] + "…";
}
