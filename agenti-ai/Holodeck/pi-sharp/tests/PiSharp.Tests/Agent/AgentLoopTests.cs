// Ported from pi: packages/agent/test/agent-loop.test.ts @ 71dca871b (cases that survive dropping the approval hooks).
using System.Linq;
using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Messages;
using PiSharp.Core.Models;
using static PiSharp.Tests.Agent.Fakes;
using PiSharp.Core.Session;

namespace PiSharp.Tests.Agent;

public class AgentLoopTests
{
    private static AgentContext NewContext(params IAgentTool[] tools) => new()
    {
        SystemPrompt = "system",
        Tools = [.. tools],
    };

    private static IAsyncEnumerable<AgentEvent> Run(AgentContext ctx, ScriptedStream stream, AgentLoopConfig? config = null, CancellationToken ct = default) =>
        AgentLoop.Run([new UserMessage("hello")], ctx, config ?? Config(), stream.Stream, ct);

    [Fact]
    public async Task ReturnedError_PreservesFailureInEventsAndHistory()
    {
        var tool = new DelegateTool { Execute = (_, _, _, _) => Task.FromResult(AgentToolResult.Error("conflict")) };
        var ctx = NewContext(tool);
        var stream = new ScriptedStream().Then(ToolCalls(Call("c1", "test_tool", Json("{}")))).Then(Text("recovered"));
        var events = await Collect(Run(ctx, stream));
        Assert.True(Assert.Single(ctx.Messages.OfType<ToolResultMessage>()).IsError);
        Assert.True(Assert.Single(events.OfType<ToolExecutionEnd>()).IsError);
    }

    [Fact]
    public async Task LargeToolTextIsHeadTailBudgetedAndFullTextSavedWithoutDroppingImages()
    {
        var fullText = string.Concat(Enumerable.Repeat("0123456789abcdef\\n", 20_000));
        var tool = new DelegateTool
        {
            Execute = (_, _, _, _) => Task.FromResult(new AgentToolResult([
                new TextContent(fullText), new ImageContent("aW1hZ2U=", "image/png")],
                new JsonObject { ["data"] = new string('x', 70_000) }))
        };
        var ctx = NewContext(tool);
        var stream = new ScriptedStream().Then(ToolCalls(Call("c1", "test_tool", Json("{}")))).Then(Text("done"));
        await Collect(AgentLoop.Run([new UserMessage("hello")], ctx,
            Config() with { ToolResultTokenBudget = 100 }, stream.Stream));

        var result = Assert.Single(ctx.Messages.OfType<ToolResultMessage>());
        var text = Assert.Single(result.Content.OfType<TextContent>()).Text;
        Assert.True(PiSharp.Core.Session.TokenEstimate.EstimateTextTokens(text) <= 100);
        Assert.Contains("tokens omitted", text);
        Assert.Contains(fullText[..20], text);
        Assert.Contains(fullText[^20..], text);
        Assert.Equal("aW1hZ2U=", Assert.Single(result.Content.OfType<ImageContent>()).Data);
        Assert.True(result.Details!["truncated"]!.GetValue<bool>());
        Assert.IsType<JsonObject>(JsonNode.Parse(result.Details.ToJsonString()));
        var path = text.Split("full output: ", StringSplitOptions.None)[1].Split(']')[0];
        Assert.Equal(fullText, await File.ReadAllTextAsync(path));
        File.Delete(path);
    }

    [Fact]
    public async Task TextResponse_EmitsExpectedEventSequence()
    {
        var ctx = NewContext();
        var stream = new ScriptedStream().Then(Text("Hello!"));

        var events = await Collect(Run(ctx, stream));

        Assert.Equal(
            [typeof(AgentStart), typeof(TurnStart), typeof(MessageStart), typeof(MessageEnd), typeof(MessageStart), typeof(MessageEnd), typeof(TurnEnd), typeof(AgentEnd)],
            events.Select(e => e.GetType()).ToArray());
        Assert.IsType<UserMessage>(((MessageStart)events[2]).Message);
        Assert.IsType<AssistantMessage>(((MessageStart)events[4]).Message);
        var end = Assert.IsType<AgentEnd>(events[^1]);
        Assert.Equal(2, end.Messages.Count);
        Assert.Equal(2, ctx.Messages.Count);
        Assert.Equal("Hello!", ((AssistantMessage)ctx.Messages[1]).Text);
        Assert.Equal(1, stream.Calls);
    }

    [Fact]
    public async Task TransformContext_RunsBeforeConvertToLlm()
    {
        var order = new List<string>();
        var ctx = NewContext();
        var stream = new ScriptedStream().Then(Text("ok"));
        var config = Config() with
        {
            TransformContext = (messages, _) =>
            {
                order.Add("transform");
                return ValueTask.FromResult<IReadOnlyList<Message>>([.. messages, new UserMessage("injected")]);
            },
            ConvertToLlm = messages =>
            {
                order.Add("convert");
                return ValueTask.FromResult(messages);
            },
        };

        await Collect(Run(ctx, stream, config));

        Assert.Equal(["transform", "convert"], order);
        Assert.Equal(2, stream.Contexts[0].Messages.Count);
        Assert.Equal("injected", ((UserMessage)stream.Contexts[0].Messages[1]).Text);
        Assert.Equal(2, ctx.Messages.Count); // transform output is not persisted
    }

    [Fact]
    public async Task ToolCall_ExecutesToolAndPropagatesUsage()
    {
        JsonObject? received = null;
        var tool = new DelegateTool
        {
            Execute = (_, args, _, _) =>
            {
                received = args;
                return Task.FromResult(AgentToolResult.FromText("result") with { Usage = new Usage(Input: 5, Output: 7) });
            },
        };
        var ctx = NewContext(tool);
        var stream = new ScriptedStream()
            .Then(ToolCalls(Call("c1", "test_tool", Json("""{"value":"x"}"""))))
            .Then(Text("done"));

        var events = await Collect(Run(ctx, stream));

        Assert.Equal(2, stream.Calls);
        Assert.Equal("x", received!["value"]!.GetValue<string>());
        var result = Assert.Single(ctx.Messages.OfType<ToolResultMessage>());
        Assert.Equal("c1", result.ToolCallId);
        Assert.Equal("result", ResultText(result));
        Assert.False(result.IsError);
        Assert.Equal(new Usage(Input: 5, Output: 7), result.Usage);
        var end = Assert.Single(events.OfType<ToolExecutionEnd>());
        Assert.False(end.IsError);
        Assert.Equal(["user", "assistant", "toolResult", "assistant"], ctx.Messages.Select(Role));
    }

    [Fact]
    public async Task ToolGuard_BlocksAfterValidationBeforeExecuting()
    {
        var executed = false;
        var tool = new DelegateTool
        {
            Execute = (_, _, _, _) => { executed = true; return Task.FromResult(AgentToolResult.FromText("ran")); },
        };
        var ctx = NewContext(tool);
        var stream = new ScriptedStream()
            .Then(ToolCalls(Call("c1", "test_tool", Json("""{"value":"danger"}"""))))
            .Then(Text("done"));
        var config = Config() with
        {
            ToolGuard = new PiSharp.Core.Guard.ToolGuard(
                [new PiSharp.Core.Guard.GuardRule("test-rule", "danger", "blocked")],
                PiSharp.Core.Guard.GuardMode.Enforce),
        };

        var events = await Collect(Run(ctx, stream, config));

        Assert.False(executed);
        var result = Assert.Single(ctx.Messages.OfType<ToolResultMessage>());
        Assert.True(result.IsError);
        Assert.Contains("blocked [test-rule]", ResultText(result));
        Assert.True(Assert.Single(events.OfType<ToolExecutionEnd>()).IsError);
    }

    private sealed class FakeReviewer(params PiSharp.Core.Guard.ActionReviewOutcome[] outcomes) : PiSharp.Core.Guard.IActionReviewer
    {
        private int _next;
        public List<string> Reviewed { get; } = [];
        public ValueTask<PiSharp.Core.Guard.ActionReviewOutcome> ReviewAsync(ToolCallContent call, PiSharp.Core.Guard.GuardVerdict verdict, AgentContext context, CancellationToken ct)
        {
            Reviewed.Add(verdict.Rule.Id);
            return ValueTask.FromResult(outcomes[Math.Min(_next++, outcomes.Length - 1)]);
        }
    }

    // plan-pi-sharp-codex item 2: an Ask runs only when the reviewer allows it; a denial teaches; repeated denials stop the turn.
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task AnAskRunsOnlyWhenTheReviewerAllowsIt(bool allowed, bool stop)
    {
        var executed = false;
        var tool = new DelegateTool
        {
            Execute = (_, _, _, _) => { executed = true; return Task.FromResult(AgentToolResult.FromText("ran")); },
        };
        var ctx = NewContext(tool);
        var stream = new ScriptedStream()
            .Then(ToolCalls(Call("c1", "test_tool", Json("""{"value":"risky"}"""))))
            .Then(Text("done"));
        var reviewer = new FakeReviewer(new PiSharp.Core.Guard.ActionReviewOutcome(allowed, "it touches another seat's branch", stop));
        var decisions = new List<string>();
        var config = Config() with
        {
            RecordGuard = (call, verdict, decision) => decisions.Add($"{call.Name} {verdict.Rule.Id} {decision}"),
            ToolGuard = new PiSharp.Core.Guard.ToolGuard(
                [new PiSharp.Core.Guard.GuardRule("ask-rule", "risky", "a reviewer decides it", action: PiSharp.Core.Guard.GuardAction.Ask)
                    { Alternative = "work on your own branch" }],
                PiSharp.Core.Guard.GuardMode.Enforce),
            ActionReviewer = reviewer,
        };

        await Collect(Run(ctx, stream, config));

        Assert.Equal(["ask-rule"], reviewer.Reviewed);
        Assert.Equal(allowed, executed);
        Assert.Equal([$"test_tool ask-rule {(allowed ? "allowed" : stop ? "denied, turn stopped" : "denied")}"], decisions);
        var result = Assert.Single(ctx.Messages.OfType<ToolResultMessage>());
        if (allowed) { Assert.Equal("ran", ResultText(result)); return; }
        Assert.True(result.IsError);
        Assert.Contains("Denied by the reviewer: it touches another seat's branch", ResultText(result));
        Assert.Contains("Instead: work on your own branch", ResultText(result));
        Assert.Equal(stop, ResultText(result).Contains("this turn stops here"));
        Assert.Equal(stop ? 1 : 2, stream.Calls); // the breaker ends the turn instead of sampling again
    }

    [Fact]
    public async Task PostToolUseFeedbackJoinsTheResult_AndUserPromptSubmitAddsContext()
    {
        if (OperatingSystem.IsWindows()) return;
        var tool = new DelegateTool { Execute = (_, _, _, _) => Task.FromResult(AgentToolResult.FromText("ran")) };
        var ctx = NewContext(tool);
        var stream = new ScriptedStream().Then(ToolCalls(Call("c1", "test_tool", Json("""{"value":"x"}""")))).Then(Text("done"));
        var config = Config() with
        {
            CommandHooks = new CommandHookRunner(new Dictionary<CommandHookEvent, IReadOnlyList<CommandHook>>
            {
                [CommandHookEvent.PostToolUse] = [new("cat >/dev/null; printf '{\"additional_context\":\"checked by the hook\"}'", "v1", TimeSpan.FromSeconds(5))],
                [CommandHookEvent.UserPromptSubmit] = [new("cat >/dev/null; printf '{\"additional_context\":\"the epic memory is current\"}'", "v1", TimeSpan.FromSeconds(5))],
            }),
            GetSteeringMessages = () => ValueTask.FromResult<IReadOnlyList<Message>>([]),
        };
        await Collect(AgentLoop.Run([new UserMessage("start")], ctx, config, stream.Stream));
        var result = Assert.Single(ctx.Messages.OfType<ToolResultMessage>());
        Assert.Contains("ran", ResultText(result));
        Assert.Contains("[hook] checked by the hook", ResultText(result));
        Assert.Contains(ctx.Messages.OfType<UserMessage>(), m => ContentText.Of(m.Content) == "[hook] the epic memory is current");
    }

    /// <summary>plan-pi-sharp-codex item 4: UserPromptSubmit is the inbox hook point, so it fires for a message that arrives
    /// while the run goes on, not only for the run's first prompt.</summary>
    [Fact]
    public async Task UserPromptSubmitFiresForAnInboxMessageThatArrivesMidRun()
    {
        if (OperatingSystem.IsWindows()) return;
        var log = Path.Combine(Path.GetTempPath(), "pi-prompt-hook-" + Guid.NewGuid().ToString("N"));
        try
        {
            var tool = new DelegateTool { Execute = (_, _, _, _) => Task.FromResult(AgentToolResult.FromText("ran")) };
            var ctx = NewContext(tool);
            var stream = new ScriptedStream().Then(ToolCalls(Call("c1", "test_tool", Json("""{"value":"x"}""")))).Then(Text("done"));
            var polls = 0;
            var config = Config() with
            {
                CommandHooks = new CommandHookRunner(new Dictionary<CommandHookEvent, IReadOnlyList<CommandHook>>
                {
                    [CommandHookEvent.UserPromptSubmit] = [new($"cat >> '{log}'; echo >> '{log}'", "v1", TimeSpan.FromSeconds(5))],
                }),
                GetSteeringMessages = () => ValueTask.FromResult<IReadOnlyList<Message>>(++polls == 2
                    ? [new UserMessage("[inbox from @team-lead-2] rebase on main first")] : []),
            };
            await Collect(AgentLoop.Run([new UserMessage("start")], ctx, config, stream.Stream));
            var fired = File.ReadAllText(log);
            Assert.Contains("start", fired);
            Assert.Contains("rebase on main first", fired);
            Assert.Equal(2, fired.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        }
        finally { File.Delete(log); }
    }

    [Fact]
    public async Task AnAskWithoutAReviewerIsRefused()
    {
        var decisions = new List<string>();
        var executed = false;
        var tool = new DelegateTool { Execute = (_, _, _, _) => { executed = true; return Task.FromResult(AgentToolResult.FromText("ran")); } };
        var ctx = NewContext(tool);
        var stream = new ScriptedStream().Then(ToolCalls(Call("c1", "test_tool", Json("""{"value":"risky"}""")))).Then(Text("done"));
        var config = Config() with
        {
            ToolGuard = new PiSharp.Core.Guard.ToolGuard(
                [new PiSharp.Core.Guard.GuardRule("ask-rule", "risky", "a reviewer decides it", action: PiSharp.Core.Guard.GuardAction.Ask)],
                PiSharp.Core.Guard.GuardMode.Enforce),
            RecordGuard = (_, verdict, decision) => decisions.Add($"{verdict.Rule.Id} {decision}"),
        };
        await Collect(Run(ctx, stream, config));
        Assert.False(executed);
        Assert.Contains("this run has no reviewer", ResultText(Assert.Single(ctx.Messages.OfType<ToolResultMessage>())));
        Assert.Equal(["ask-rule unreviewed"], decisions);
    }

    [Fact]
    public async Task ToolkitCall_RunsTheNamedToolUnderTheCallsIdWithItsArgumentsValidated()
    {
        JsonObject? seen = null;
        var inner = new DelegateTool
        {
            Name = "kb__kb_artifacts",
            Parameters = Json("""{"type":"object","properties":{"slug":{"type":"string"}},"required":["slug"]}"""),
            Execute = (_, args, _, _) => { seen = args; return Task.FromResult(AgentToolResult.FromText("got it")); },
        };
        IReadOnlyList<IAgentTool> registry = [inner];
        var toolkit = new ToolDiscovery(() => registry);
        var ctx = NewContext(inner, toolkit);
        var stream = new ScriptedStream()
            .Then(ToolCalls(
                Call("c1", "toolkit", Json("""{"op":"call","name":"kb__kb_artifacts","args":{"slug":"wi-1"}}""")),
                Call("c2", "toolkit", Json("""{"op":"call","name":"kb__kb_artifacts","args":{}}""")),
                Call("c3", "toolkit", Json("""{"op":"call","name":"kb__nope"}"""))))
            .Then(Text("done"));

        await Collect(Run(ctx, stream));

        Assert.Equal("wi-1", (string?)seen!["slug"]);
        var results = ctx.Messages.OfType<ToolResultMessage>().ToArray();
        Assert.Equal(["c1", "c2", "c3"], results.Select(r => r.ToolCallId));
        Assert.All(results, r => Assert.Equal("toolkit", r.ToolName));
        Assert.Equal("got it", ResultText(results[0]));
        Assert.True(results[1].IsError);
        Assert.True(results[2].IsError);
        Assert.Contains("No permitted tool 'kb__nope'", ResultText(results[2]));
        // The advertised list was the same on both requests: the toolkit only, the prefix stays cached.
        Assert.All(stream.Contexts, c => Assert.Equal(["toolkit"], c.Tools.Select(t => t.Name)));
    }

    [Fact]
    public async Task ToolkitCall_IsGuardedAsTheNamedTool()
    {
        var executed = false;
        var inner = new DelegateTool { Execute = (_, _, _, _) => { executed = true; return Task.FromResult(AgentToolResult.FromText("ran")); } };
        IReadOnlyList<IAgentTool> registry = [inner];
        var ctx = NewContext(inner, new ToolDiscovery(() => registry));
        var stream = new ScriptedStream()
            .Then(ToolCalls(Call("c1", "toolkit", Json("""{"op":"call","name":"test_tool","args":{"value":"danger"}}"""))))
            .Then(Text("done"));
        var config = Config() with
        {
            ToolGuard = new PiSharp.Core.Guard.ToolGuard(
                [new PiSharp.Core.Guard.GuardRule("test-rule", "danger", "blocked")],
                PiSharp.Core.Guard.GuardMode.Enforce),
        };

        await Collect(Run(ctx, stream, config));

        Assert.False(executed);
        Assert.Contains("blocked [test-rule]", ResultText(Assert.Single(ctx.Messages.OfType<ToolResultMessage>())));
    }

    [Fact]
    public async Task CommandHook_PreToolUseRefusalIsReturnedToModelWithoutExecutingTool()
    {
        if (OperatingSystem.IsWindows()) return;
        var executed = false;
        var tool = new DelegateTool { Execute = (_, _, _, _) => { executed = true; return Task.FromResult(AgentToolResult.FromText("ran")); } };
        var ctx = NewContext(tool);
        var stream = new ScriptedStream().Then(ToolCalls(Call("c1", "test_tool"))).Then(Text("done"));
        var config = Config() with
        {
            CommandHooks = new CommandHookRunner(new Dictionary<CommandHookEvent, CommandHook>
            {
                [CommandHookEvent.PreToolUse] = new("cat >/dev/null; printf '{\"block\":true,\"reason\":\"refused by hook\"}'", "test-v1", TimeSpan.FromSeconds(2)),
            }),
        };

        await Collect(Run(ctx, stream, config));

        Assert.False(executed);
        Assert.Contains("refused by hook", ResultText(Assert.Single(ctx.Messages.OfType<ToolResultMessage>())));
        Assert.Equal(2, stream.Calls);
    }

    [Fact]
    public async Task CommandHook_StopCanRequestOneContinuationWithReason()
    {
        if (OperatingSystem.IsWindows()) return;
        var ctx = NewContext();
        var stream = new ScriptedStream().Then(Text("still blocked on X")).Then(Text("resolved"));
        var config = Config() with
        {
            MaxStopBlocks = 1,
            CommandHooks = new CommandHookRunner(new Dictionary<CommandHookEvent, CommandHook>
            {
                [CommandHookEvent.Stop] = new("cat >/dev/null; printf '{\"block\":true,\"reason\":\"recheck X\"}'", "test-v1", TimeSpan.FromSeconds(2)),
            }),
        };

        await Collect(Run(ctx, stream, config));

        Assert.Equal(2, stream.Calls);
        Assert.Contains(ctx.Messages.OfType<UserMessage>(), message => message.Text.Contains("recheck X", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DegenerateReply_IsCutAndTheModelGetsOneMoreTurn()
    {
        // coder-06-pi4, board 605743: one sentence repeated until the limit, delivered as the task's answer.
        var looped = "Analysis so far. " + string.Concat(Enumerable.Repeat(
            "The solution: I need to check if the ROM is actually supposed to process keyboard input. ", 60));
        var ctx = NewContext();
        var stream = new ScriptedStream().Then(Text(looped)).Then(Text(looped)).Then(Text("unreached"));

        var events = await Collect(Run(ctx, stream));

        Assert.Equal(2, stream.Calls); // one retry, never a second
        var ends = events.OfType<TurnEnd>().ToArray();
        Assert.False(ends[0].Final);
        Assert.True(ends[1].Final);
        var first = (AssistantMessage)ctx.Messages[1];
        Assert.EndsWith("[cut by the harness: not delivered]", first.Text);
        Assert.True(first.Text.Length < 1300);
        Assert.Contains("repeated the same sentences", Assert.IsType<UserMessage>(ctx.Messages[2]).Text);
    }

    [Fact]
    public async Task LengthStopWithoutToolCalls_IsNoAnswer()
    {
        var ctx = NewContext();
        var stream = new ScriptedStream()
            .Then(Assistant(StopReason.Length, new TextContent("half a thou")))
            .Then(Text("done"));

        await Collect(Run(ctx, stream));

        Assert.Equal(2, stream.Calls);
        Assert.Contains("output limit", ctx.Messages.OfType<UserMessage>().Last().Text);
        Assert.Equal("done", ((AssistantMessage)ctx.Messages[^1]).Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DegenerateReply_LeavesLongVariedReportsAlone(bool withRepeatedHeading)
    {
        var lines = Enumerable.Range(0, 80).Select(i => $"Step {i}: checked register pair {i * 7} against the trace and it matched");
        if (withRepeatedHeading) lines = lines.SelectMany((l, i) => i % 10 == 0 ? new[] { "Next we look at the following part", l } : [l]);
        Assert.Null(DegenerateReply.Why(string.Join(".\n", lines), cutByLength: false));
    }

    [Fact]
    public async Task LengthStop_FailsToolCallsWithoutExecuting()
    {
        var executed = false;
        var tool = new DelegateTool { Execute = (_, _, _, _) => { executed = true; return Task.FromResult(AgentToolResult.FromText("ran")); } };
        var ctx = NewContext(tool);
        var stream = new ScriptedStream()
            .Then(Assistant(StopReason.Length, Call("c1", "test_tool", Json("""{"value":"trunc"}"""))))
            .Then(Text("recovered"));

        var events = await Collect(Run(ctx, stream));

        Assert.False(executed);
        Assert.Equal(2, stream.Calls);
        var result = Assert.Single(ctx.Messages.OfType<ToolResultMessage>());
        Assert.True(result.IsError);
        Assert.Contains("output token limit", ResultText(result));
        Assert.Single(events.OfType<ToolExecutionStart>());
        Assert.True(Assert.Single(events.OfType<ToolExecutionEnd>()).IsError);
    }

    [Fact]
    public async Task PrepareArguments_RewritesBeforeValidation()
    {
        JsonObject? received = null;
        var tool = new DelegateTool
        {
            Parameters = Json("""{"type":"object","properties":{"value":{"type":"string"}},"required":["value"]}"""),
            Prepare = raw => new JsonObject { ["value"] = raw?["raw"]?.DeepClone() },
            Execute = (_, args, _, _) => { received = args; return Task.FromResult(AgentToolResult.FromText("ok")); },
        };
        var ctx = NewContext(tool);
        var stream = new ScriptedStream()
            .Then(ToolCalls(Call("c1", "test_tool", Json("""{"raw":"5"}"""))))
            .Then(Text("done"));

        await Collect(Run(ctx, stream));

        Assert.Equal("5", received!["value"]!.GetValue<string>());
    }

    [Fact]
    public async Task ParallelExecution_EndsInCompletionOrder_MessagesInSourceOrder()
    {
        // The gate's continuation runs on the pool, and slow ends a little after it: with an inline continuation slow could
        // end inside fast's own call, before fast did (1 run in 5 failed at load 42 on host-01, 2026-10-29).
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var slow = new DelegateTool
        {
            Name = "slow",
            Execute = async (_, _, _, _) => { await gate.Task; await Task.Delay(200); return AgentToolResult.FromText("slow"); },
        };
        var fast = new DelegateTool
        {
            Name = "fast",
            Execute = (_, _, _, _) => { gate.TrySetResult(); return Task.FromResult(AgentToolResult.FromText("fast")); },
        };
        var ctx = NewContext(slow, fast);
        var stream = new ScriptedStream()
            .Then(ToolCalls(Call("c-slow", "slow"), Call("c-fast", "fast")))
            .Then(Text("done"));

        var events = await Collect(Run(ctx, stream));

        Assert.Equal(["c-fast", "c-slow"], events.OfType<ToolExecutionEnd>().Select(e => e.ToolCallId));
        Assert.Equal(["c-slow", "c-fast"], events.OfType<MessageStart>().Select(e => e.Message).OfType<ToolResultMessage>().Select(m => m.ToolCallId));
        Assert.Equal(["c-slow", "c-fast"], ctx.Messages.OfType<ToolResultMessage>().Select(m => m.ToolCallId));
    }

    [Fact]
    public async Task SequentialTool_ForcesSequentialBatch()
    {
        var order = new List<string>();
        var slowSequential = new DelegateTool
        {
            Name = "slow",
            ExecutionMode = ToolExecutionMode.Sequential,
            Execute = async (_, _, _, _) => { await Task.Delay(50); order.Add("slow"); return AgentToolResult.FromText("slow"); },
        };
        var fast = new DelegateTool
        {
            Name = "fast",
            Execute = (_, _, _, _) => { order.Add("fast"); return Task.FromResult(AgentToolResult.FromText("fast")); },
        };
        var ctx = NewContext(slowSequential, fast);
        var stream = new ScriptedStream()
            .Then(ToolCalls(Call("c1", "slow"), Call("c2", "fast")))
            .Then(Text("done"));

        var events = await Collect(Run(ctx, stream));

        Assert.Equal(["slow", "fast"], order);
        Assert.Equal(["c1", "c2"], events.OfType<ToolExecutionEnd>().Select(e => e.ToolCallId));
    }

    [Fact]
    public async Task SteeringMessages_InjectedAfterToolBatch()
    {
        var polls = 0;
        var ctx = NewContext(new DelegateTool());
        var stream = new ScriptedStream()
            .Then(ToolCalls(Call("c1", "test_tool")))
            .Then(Text("done"));
        var config = Config() with
        {
            GetSteeringMessages = () =>
            {
                polls++;
                IReadOnlyList<Message> result = polls == 2 ? [new UserMessage("steer")] : [];
                return ValueTask.FromResult(result);
            },
        };

        var events = await Collect(Run(ctx, stream, config));

        Assert.Equal(2, stream.Calls);
        var second = stream.Contexts[1].Messages;
        Assert.Equal("steer", ((UserMessage)second[^1]).Text);
        Assert.Equal(["user", "assistant", "toolResult", "user", "assistant"], ctx.Messages.Select(Role));
        Assert.Equal(2, events.OfType<TurnStart>().Count());
    }

    [Fact]
    public async Task FollowUpMessages_StartAnotherTurn()
    {
        var polls = 0;
        var ctx = NewContext();
        var stream = new ScriptedStream().Then(Text("first")).Then(Text("second"));
        var config = Config() with
        {
            GetFollowUpMessages = () =>
            {
                polls++;
                IReadOnlyList<Message> result = polls == 1 ? [new UserMessage("follow-up")] : [];
                return ValueTask.FromResult(result);
            },
        };

        await Collect(Run(ctx, stream, config));

        Assert.Equal(2, stream.Calls);
        Assert.Equal(2, polls);
        Assert.Equal(["user", "assistant", "user", "assistant"], ctx.Messages.Select(Role));
    }

    [Fact]
    public async Task PrepareNextTurn_ReceivesSnapshotAndSwapsContext()
    {
        var prepareCalls = 0;
        TurnContext? seen = null;
        var ctx = NewContext(new DelegateTool());
        var stream = new ScriptedStream()
            .Then(ToolCalls(Call("c1", "test_tool")))
            .Then(Text("done"));
        var config = Config() with
        {
            PrepareNextTurn = turn =>
            {
                prepareCalls++;
                seen = turn;
                var replacement = new AgentContext
                {
                    SystemPrompt = "second prompt",
                    Messages = [.. turn.Context.Messages],
                    Tools = turn.Context.Tools,
                };
                return ValueTask.FromResult<TurnUpdate?>(new TurnUpdate(Context: replacement));
            },
        };

        await Collect(Run(ctx, stream, config));

        Assert.Equal(1, prepareCalls);
        Assert.Equal(2, stream.Calls);
        Assert.Equal("system", stream.Contexts[0].SystemPrompt);
        Assert.Equal("second prompt", stream.Contexts[1].SystemPrompt);
        Assert.Single(seen!.ToolResults);
        Assert.Equal(3, seen.NewMessages.Count);
        Assert.Equal(3, ctx.Messages.Count); // original context untouched after the swap
    }

    [Fact]
    public async Task ShouldStopAfterTurn_StopsWithoutPollingQueues()
    {
        var steeringPolls = 0;
        var followUpPolls = 0;
        var ctx = NewContext(new DelegateTool());
        var stream = new ScriptedStream().Then(ToolCalls(Call("c1", "test_tool")));
        var config = Config() with
        {
            ShouldStopAfterTurn = _ => ValueTask.FromResult(true),
            GetSteeringMessages = () => { steeringPolls++; return ValueTask.FromResult<IReadOnlyList<Message>>([]); },
            GetFollowUpMessages = () => { followUpPolls++; return ValueTask.FromResult<IReadOnlyList<Message>>([]); },
        };

        var events = await Collect(Run(ctx, stream, config));

        Assert.Equal(
            [
                typeof(AgentStart), typeof(TurnStart),
                typeof(MessageStart), typeof(MessageEnd),
                typeof(MessageStart), typeof(MessageEnd),
                typeof(ToolExecutionStart), typeof(ToolExecutionEnd),
                typeof(MessageStart), typeof(MessageEnd),
                typeof(TurnEnd), typeof(AgentEnd),
            ],
            events.Select(e => e.GetType()).ToArray());
        Assert.Equal(1, stream.Calls);
        Assert.Equal(1, steeringPolls);
        Assert.Equal(0, followUpPolls);
    }

    [Fact]
    public async Task Terminate_StopsAfterBatch()
    {
        var tool = new DelegateTool { Execute = (_, _, _, _) => Task.FromResult(AgentToolResult.FromText("bye") with { Terminate = true }) };
        var ctx = NewContext(tool);
        var stream = new ScriptedStream().Then(ToolCalls(Call("c1", "test_tool")));

        var events = await Collect(Run(ctx, stream));

        Assert.Equal(1, stream.Calls);
        Assert.Equal(["user", "assistant", "toolResult"], ctx.Messages.Select(Role));
        Assert.Single(events.OfType<TurnEnd>());
        Assert.IsType<AgentEnd>(events[^1]);
    }

    [Fact]
    public async Task ParallelBatch_ContinuesUnlessEveryResultTerminates()
    {
        var stopper = new DelegateTool { Name = "stop", Execute = (_, _, _, _) => Task.FromResult(AgentToolResult.FromText("bye") with { Terminate = true }) };
        var normal = new DelegateTool { Name = "go" };
        var ctx = NewContext(stopper, normal);
        var stream = new ScriptedStream()
            .Then(ToolCalls(Call("c1", "stop"), Call("c2", "go")))
            .Then(Text("continued"));

        await Collect(Run(ctx, stream));

        Assert.Equal(2, stream.Calls);
    }

    [Fact]
    public void Continue_ThrowsWithNoMessages()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => AgentLoop.Continue(NewContext(), Config(), new ScriptedStream().Stream));
        Assert.Equal("Cannot continue: no messages in context", ex.Message);
    }

    [Fact]
    public void Continue_ThrowsWhenLastMessageIsAssistant()
    {
        var ctx = NewContext();
        ctx.Messages.Add(new UserMessage("hi"));
        ctx.Messages.Add(Text("there"));

        var ex = Assert.Throws<InvalidOperationException>(() => AgentLoop.Continue(ctx, Config(), new ScriptedStream().Stream));
        Assert.Equal("Cannot continue from message role: assistant", ex.Message);
    }

    [Fact]
    public async Task Continue_EmitsNoUserEventsAndReturnsOnlyNewMessages()
    {
        var ctx = NewContext();
        ctx.Messages.Add(new UserMessage("resume me"));
        var stream = new ScriptedStream().Then(Text("resumed"));

        var events = await Collect(AgentLoop.Continue(ctx, Config(), stream.Stream));

        Assert.DoesNotContain(events, e => e is MessageStart { Message: UserMessage });
        var end = Assert.IsType<AgentEnd>(events[^1]);
        Assert.Single(end.Messages);
        Assert.Equal(2, ctx.Messages.Count);
        Assert.Equal("resume me", ((UserMessage)stream.Contexts[0].Messages[0]).Text);
    }

    [Fact]
    public async Task CustomMessages_DroppedByDefaultAndConvertedByCustomConvertToLlm()
    {
        var ctx = NewContext();
        ctx.Messages.Add(new NoteMessage("remember this"));
        var stream = new ScriptedStream().Then(Text("a")).Then(Text("b"));

        await Collect(Run(ctx, stream));
        Assert.Single(stream.Contexts[0].Messages);
        Assert.IsType<UserMessage>(stream.Contexts[0].Messages[0]);

        var config = Config() with
        {
            ConvertToLlm = messages => ValueTask.FromResult<IReadOnlyList<Message>>(
                messages.Select(m => m is NoteMessage note ? new UserMessage($"note: {note.Note}") : m).ToList()),
        };
        await Collect(Run(ctx, stream, config));
        Assert.Equal("note: remember this", ((UserMessage)stream.Contexts[1].Messages[0]).Text);
    }

    [Fact]
    public async Task UnknownTool_ProducesErrorResult()
    {
        var ctx = NewContext();
        var stream = new ScriptedStream().Then(ToolCalls(Call("c1", "nope"))).Then(Text("done"));

        var events = await Collect(Run(ctx, stream));

        var result = Assert.Single(ctx.Messages.OfType<ToolResultMessage>());
        Assert.True(result.IsError);
        Assert.Equal("Tool nope not found", ResultText(result));
        Assert.True(Assert.Single(events.OfType<ToolExecutionEnd>()).IsError);
    }

    [Fact]
    public async Task ValidationFailure_ProducesErrorResultWithoutExecuting()
    {
        var executed = false;
        var tool = new DelegateTool
        {
            Parameters = Json("""{"type":"object","properties":{"value":{"type":"string"}},"required":["value"]}"""),
            Execute = (_, _, _, _) => { executed = true; return Task.FromResult(AgentToolResult.FromText("ran")); },
        };
        var ctx = NewContext(tool);
        var stream = new ScriptedStream().Then(ToolCalls(Call("c1", "test_tool", Json("""{"value":{"x":1}}""")))).Then(Text("done"));

        await Collect(Run(ctx, stream));

        Assert.False(executed);
        var result = Assert.Single(ctx.Messages.OfType<ToolResultMessage>());
        Assert.True(result.IsError);
        Assert.StartsWith("Validation failed for tool \"test_tool\"", ResultText(result));
        Assert.Contains("value: Expected string, received object", ResultText(result));
    }

    [Fact]
    public async Task UnresponsiveToolIsAbandonedAtLoopDeadlineAndTurnContinues()
    {
        var never = new TaskCompletionSource<AgentToolResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tool = new DelegateTool { Execute = (_, _, _, _) => never.Task };
        var ctx = NewContext(tool);
        var stream = new ScriptedStream().Then(ToolCalls(Call("c1", "test_tool"))).Then(Text("continued"));
        var config = Config() with { ToolCallTimeout = TimeSpan.FromMilliseconds(40) };

        var events = await Collect(Run(ctx, stream, config));

        Assert.Contains("tool test_tool aborted after 0.04 s (deadline)", ResultText(Assert.Single(ctx.Messages.OfType<ToolResultMessage>())));
        Assert.Contains(events, e => e is ToolExecutionDeadline { ToolName: "test_tool", Deadline: var deadline } && deadline == config.ToolCallTimeout);
        Assert.Equal("continued", ((AssistantMessage)ctx.Messages[^1]).Text);
        never.SetResult(AgentToolResult.FromText("late"));
    }

    [Fact]
    public async Task ToolThrows_ProducesErrorResult()
    {
        var tool = new DelegateTool { Execute = (_, _, _, _) => throw new InvalidOperationException("boom") };
        var ctx = NewContext(tool);
        var stream = new ScriptedStream().Then(ToolCalls(Call("c1", "test_tool"))).Then(Text("done"));

        var events = await Collect(Run(ctx, stream));

        var result = Assert.Single(ctx.Messages.OfType<ToolResultMessage>());
        Assert.True(result.IsError);
        Assert.Equal("boom", ResultText(result));
        Assert.Equal(2, stream.Calls);
        Assert.Single(events.OfType<ToolExecutionEnd>());
    }

    [Fact]
    public async Task ToolUpdates_EmitToolExecutionUpdate()
    {
        var tool = new DelegateTool
        {
            Execute = (_, _, onUpdate, _) =>
            {
                onUpdate?.Invoke(AgentToolResult.FromText("50%"));
                return Task.FromResult(AgentToolResult.FromText("100%"));
            },
        };
        var ctx = NewContext(tool);
        var stream = new ScriptedStream().Then(ToolCalls(Call("c1", "test_tool"))).Then(Text("done"));

        var events = await Collect(Run(ctx, stream));

        var update = Assert.Single(events.OfType<ToolExecutionUpdate>());
        Assert.Equal("50%", update.PartialResult.Text);
        Assert.True(events.FindIndex(e => e is ToolExecutionUpdate) < events.FindIndex(e => e is ToolExecutionEnd));
    }

    [Fact]
    public async Task StreamError_EndsRunWithEmptyTurnEnd()
    {
        var ctx = NewContext();
        var stream = new ScriptedStream().Then(Error("model exploded"));

        var events = await Collect(Run(ctx, stream));

        Assert.Equal(1, stream.Calls);
        var turnEnd = Assert.Single(events.OfType<TurnEnd>());
        Assert.Empty(turnEnd.ToolResults);
        Assert.Equal(StopReason.Error, turnEnd.Message.StopReason);
        Assert.Equal("model exploded", turnEnd.Message.ErrorMessage);
        Assert.IsType<AgentEnd>(events[^1]);
        Assert.Equal(2, ctx.Messages.Count);
    }

    [Fact]
    public async Task StreamThrows_ConvertedToErrorTurn()
    {
        var ctx = NewContext();
        var stream = new ScriptedStream { ThrowAfterStart = new HttpRequestException("kaboom") };

        var events = await Collect(Run(ctx, stream));

        var turnEnd = Assert.Single(events.OfType<TurnEnd>());
        Assert.Equal(StopReason.Error, turnEnd.Message.StopReason);
        Assert.Equal("kaboom", turnEnd.Message.ErrorMessage);
        Assert.Equal(["user", "assistant"], ctx.Messages.Select(Role));
        Assert.Equal(1, events.OfType<MessageStart>().Count(e => e.Message is AssistantMessage));
    }

    [Fact]
    public async Task Cancellation_AbortsToolAndFollowingStream()
    {
        using var cts = new CancellationTokenSource();
        var tool = new DelegateTool
        {
            Execute = async (_, _, _, ct) =>
            {
                cts.Cancel();
                await Task.Delay(Timeout.Infinite, ct);
                return AgentToolResult.FromText("unreachable");
            },
        };
        var ctx = NewContext(tool);
        var stream = new ScriptedStream().Then(ToolCalls(Call("c1", "test_tool")));

        var events = await Collect(Run(ctx, stream, ct: cts.Token));

        var result = Assert.Single(ctx.Messages.OfType<ToolResultMessage>());
        Assert.True(result.IsError);
        Assert.Equal("Operation aborted", ResultText(result));
        Assert.Equal(2, stream.Calls);
        Assert.Equal(StopReason.Aborted, events.OfType<TurnEnd>().Last().Message.StopReason);
        Assert.IsType<AgentEnd>(events[^1]);
    }

    [Fact]
    public async Task StreamOptions_ComeFromConfig()
    {
        var ctx = NewContext();
        var stream = new ScriptedStream().Then(Text("ok"));
        var config = Config() with { MaxTokens = 123, Temperature = 0.5, Reasoning = ThinkingLevel.High };

        await Collect(Run(ctx, stream, config));

        var options = Assert.Single(stream.Options);
        Assert.Equal(123, options.MaxTokens);
        Assert.Equal(0.5, options.Temperature);
        Assert.Equal(ThinkingLevel.High, options.Reasoning);
    }

    [Fact]
    public async Task TextToolCallInAssistantContent_IsRepairedAndExecuted()
    {
        JsonObject? received = null;
        var tool = new DelegateTool
        {
            Execute = (_, args, _, _) =>
            {
                received = args;
                return Task.FromResult(AgentToolResult.FromText("from-text"));
            },
        };
        var ctx = NewContext(tool);
        var stream = new ScriptedStream()
            .Then(Text("""<tool_call>{"name":"test_tool","arguments":{"value":"repaired"}}</tool_call>"""))
            .Then(Text("done"));

        await Collect(Run(ctx, stream));

        Assert.Equal(2, stream.Calls);
        Assert.Equal("repaired", received!["value"]!.GetValue<string>());
        Assert.Equal("from-text", ResultText(Assert.Single(ctx.Messages.OfType<ToolResultMessage>())));
        Assert.Equal(StopReason.ToolUse, ctx.Messages.OfType<AssistantMessage>().First().StopReason);
    }

    [Fact]
    public async Task IdenticalToolCalls_StopAfterLimitWithoutExecutingTheLast()
    {
        var executed = 0;
        var tool = new DelegateTool
        {
            Execute = (_, _, _, _) =>
            {
                executed++;
                return Task.FromResult(AgentToolResult.FromText("ok"));
            },
        };
        var same = ToolCalls(Call("c1", "test_tool", Json("""{"value":"x"}""")));
        var ctx = NewContext(tool);
        var stream = new ScriptedStream().Then(same).Then(same).Then(same).Then(Text("reconsidered"));

        var events = await Collect(Run(ctx, stream));

        Assert.Equal(4, stream.Calls);
        Assert.Equal(2, executed);
        var results = ctx.Messages.OfType<ToolResultMessage>().ToList();
        Assert.Equal(3, results.Count);
        Assert.False(results[0].IsError);
        Assert.False(results[1].IsError);
        Assert.True(results[2].IsError);
        Assert.Contains("repeated 3 times", ResultText(results[2]));
        Assert.IsType<AgentEnd>(events[^1]);
    }

    /// <summary>2026-10-20: the demo seat's desktop__act pressing one key three times was refused as a loop and its task
    /// ended, twice in one night. An input action's identical batches get MaxIdenticalInputCalls (8).</summary>
    [Fact]
    public async Task IdenticalInputActions_RunUpToTheirOwnLimitAndTheRefusalSaysLookFirst()
    {
        var executed = 0;
        var key = new DelegateTool
        {
            Name = "desktop__act",
            InputAction = true,
            Execute = (_, _, _, _) => { executed++; return Task.FromResult(AgentToolResult.FromText("pressed")); },
        };
        var down = ToolCalls(Call("c1", "desktop__act", Json("""{"value":"Down"}""")));
        var ctx = NewContext(key);
        var stream = new ScriptedStream();
        for (var i = 0; i < 8; i++) stream = stream.Then(down);
        stream = stream.Then(Text("done"));

        await Collect(Run(ctx, stream));

        Assert.Equal(7, executed);
        var results = ctx.Messages.OfType<ToolResultMessage>().ToList();
        Assert.All(results.Take(7), r => Assert.False(r.IsError));
        Assert.True(results[7].IsError);
        Assert.Contains("repeated 8 times", ResultText(results[7]));
        Assert.Contains("look at the current state first", ResultText(results[7]));
    }

    [Fact]
    public async Task IdenticalToolCalls_AfterTheRetryTheGuardEndsTheRunAndSaysSo()
    {
        // Team watch 2026-10-03: pi4's guard-ended turn was seen as "still calling tools", so its task never closed.
        var same = ToolCalls(Call("c1", "test_tool", Json("""{"value":"x"}""")));
        var ctx = NewContext(new DelegateTool());
        var stream = new ScriptedStream();
        for (var i = 0; i < 9; i++) stream = stream.Then(same);
        var finals = new List<bool>();
        var config = Config() with
        {
            ShouldStopAfterTurn = turn => { finals.Add(turn.Final); return ValueTask.FromResult(false); },
        };

        var events = await Collect(Run(ctx, stream, config));

        // Two retries (LoopGuardRetries), each after three identical batches; the third trip ends the run.
        Assert.Equal(9, stream.Calls);
        Assert.Equal([false, false, false, false, false, false, false, false, true], finals);
        var turnEnds = events.OfType<TurnEnd>().ToList();
        Assert.True(turnEnds[^1].Final);
        Assert.All(turnEnds.SkipLast(1), t => Assert.False(t.Final));
        Assert.IsType<AgentEnd>(events[^1]);
    }

    [Fact]
    public async Task RunawayChatPosts_OnlyTheCapRunsAndEveryCallGetsAResult()
    {
        var posted = 0;
        var post = new DelegateTool
        {
            Name = "mcp__chat__chat_post",
            Execute = (_, _, _, _) =>
            {
                Interlocked.Increment(ref posted);
                return Task.FromResult(AgentToolResult.FromText("posted"));
            },
        };
        var calls = Enumerable.Range(0, 40)
            .Select(i => Call($"c{i}", "mcp__chat__chat_post", Json($$"""{"value":"Done {{i}}"}""")))
            .ToArray();
        var ctx = NewContext(post);
        var stream = new ScriptedStream().Then(ToolCalls(calls)).Then(Text("ok"));

        await Collect(Run(ctx, stream));

        Assert.Equal(10, posted);
        var results = ctx.Messages.OfType<ToolResultMessage>().ToList();
        Assert.Equal(40, results.Count);
        Assert.Equal(30, results.Count(r => r.IsError));
        Assert.Contains("40 chat posts and at most 10", ResultText(results.First(r => r.IsError)));
        Assert.Equal(2, stream.Calls);
    }

    [Fact]
    public void ToolCallCap_KeepsTheFirstCallsAndCountsPostsSeparately()
    {
        var calls = new[]
        {
            Call("a", "read", Json("""{"path":"a"}""")), Call("b", "chat_post", Json("""{"body":"b"}""")),
            Call("c", "chat_post", Json("""{"body":"c"}""")), Call("d", "read", Json("""{"path":"d"}""")),
            Call("e", "read", Json("""{"path":"e"}""")),
        };

        var (allowed, rejected) = AgentLoop.SplitByCaps(calls, maxCalls: 3, maxPosts: 1);

        Assert.Equal(["a", "b", "d"], allowed.Select(c => c.Id));
        Assert.Equal(["c", "e"], rejected.Select(r => r.Call.Id));
        Assert.Empty(AgentLoop.SplitByCaps(calls, maxCalls: 0, maxPosts: 0).Rejected);
    }

    [Fact]
    public void ToolCallCap_RejectsIdenticalCallsInOneMessage_WhateverTheKeyOrder()
    {
        var calls = new[]
        {
            Call("a", "kb_get", Json("""{"slug":"x","doc":"d"}""")), Call("b", "kb_get", Json("""{"doc":"d","slug":"x"}""")),
            Call("c", "kb_get", Json("""{"slug":"y","doc":"d"}""")),
        };

        var (allowed, rejected) = AgentLoop.SplitByCaps(calls, maxCalls: 0, maxPosts: 0);

        Assert.Equal(["a", "c"], allowed.Select(c => c.Id));
        var (call, reason) = Assert.Single(rejected);
        Assert.Equal("b", call.Id);
        Assert.Contains("identical to call a", reason);
    }

    [Fact]
    public void ToolCallCap_AllowsOnePostPerPlaceInOneMessage()
    {
        var calls = new[]
        {
            Call("a", "mcp__chat__chat_post", Json("""{"room":"ops","body":"one"}""")),
            Call("b", "mcp__chat__chat_post", Json("""{"room":"ops","body":"two"}""")),
            Call("c", "mcp__chat__chat_post", Json("""{"thread":42,"body":"three"}""")),
            Call("d", "mcp__chat__chat_post", Json("""{"thread":42,"body":"four"}""")),
            Call("e", "mcp__chat__chat_post", Json("""{"to":"pi2","body":"five"}""")),
        };

        var (allowed, rejected) = AgentLoop.SplitByCaps(calls, maxCalls: 0, maxPosts: 0);

        Assert.Equal(["a", "c", "e"], allowed.Select(c => c.Id));
        Assert.Equal(["b", "d"], rejected.Select(r => r.Call.Id));
        Assert.Contains("already posts to room ops", rejected[0].Reason);
        Assert.Contains("already posts to thread 42", rejected[1].Reason);
    }

    [Fact]
    public void ToolCallCap_RefusesARepostToTheSamePlaceUntilOtherWorkIsDone()
    {
        static ToolResultMessage Ok(string id) => new() { ToolCallId = id, ToolName = "chat__chat_post", Content = [] };
        var since = new HashSet<string>(StringComparer.Ordinal);

        // agent-maker-3, 2026-10-06: one post per model call, each a rewording of the last.
        var first = new[] { Call("a", "chat__chat_post", Json("""{"room":"ops","body":"OPS CHECK complete"}""")) };
        Assert.Empty(AgentLoop.SplitByCaps(first, 0, 0, since).Rejected);
        AgentLoop.NotePostsSinceWork(since, first, [Ok("a")]);

        var reworded = new[]
        {
            Call("b", "chat__chat_post", Json("""{"room":"ops","body":"OPS CHECK done"}""")),
            Call("c", "chat__chat_post", Json("""{"thread":42,"body":"elsewhere"}""")),
        };
        var (allowed, rejected) = AgentLoop.SplitByCaps(reworded, 0, 0, since);
        Assert.Equal(["c"], allowed.Select(c => c.Id));
        Assert.Contains("already posted to room ops and have done nothing else since", Assert.Single(rejected).Reason);

        // Any other tool call is work: the next post to ops (a REVIEW after PICKED UP, say) goes through.
        AgentLoop.NotePostsSinceWork(since, [Call("d", "read", Json("""{"path":"x"}"""))], []);
        Assert.Empty(AgentLoop.SplitByCaps(reworded, 0, 0, since).Rejected);
    }

    [Fact]
    public async Task SmallContext_AdvertisesAtMostEightTools()
    {
        var tools = Enumerable.Range(1, 12).Select(i => new DelegateTool { Name = $"t{i}" }).ToArray();
        var ctx = NewContext(tools);
        var stream = new ScriptedStream().Then(Text("ok"));
        var config = Config() with { Model = Model with { ContextWindow = 4096 } };

        await Collect(Run(ctx, stream, config));

        Assert.Equal(8, stream.Contexts[0].Tools.Count);
        Assert.Equal(12, ctx.Tools.Count);
    }

    /// <summary>digital-scribe, 2026-10-20: each request asks for max_tokens = min(output cap, window - its estimated
    /// prompt). A 200k model capped at 131,072 writes up to 131,072 beside a 50k prompt and ~50,000 beside a 150k one.</summary>
    [Theory]
    [InlineData(150_000, 50_000)]
    [InlineData(50_000, 131_072)]
    public async Task EachRequestAsksForTheOutputItsWindowLeaves(int promptTokens, int expected)
    {
        var stream = new ScriptedStream().Then(Text("ok"));
        var config = Config() with { Model = Model with { ContextWindow = 200_000, MaxOutputTokens = 131_072 } };

        await Collect(AgentLoop.Run([new UserMessage(new string('x', promptTokens * TokenEstimate.CharsPerToken))], NewContext(), config, stream.Stream));

        // About: the system prompt and the message framing count too.
        Assert.InRange(Assert.Single(stream.Options).MaxTokens!.Value, expected - 100, expected);
        Assert.Equal(131_072, config.Model.MaxOutputTokens); // the model is not changed, only this request
    }

    [Fact]
    public async Task APromptThatFillsTheWindowIsRefusedWithoutCallingTheModel()
    {
        var stream = new ScriptedStream().Then(Text("ok"));
        var config = Config() with { Model = Model with { ContextWindow = 200_000, MaxOutputTokens = 131_072 } };

        var events = await Collect(AgentLoop.Run([new UserMessage(new string('x', 200_000 * TokenEstimate.CharsPerToken))], NewContext(), config, stream.Stream));

        Assert.Equal(0, stream.Calls);
        var reply = events.OfType<MessageEnd>().Select(e => e.Message).OfType<AssistantMessage>().Last();
        Assert.Equal("context_length_exceeded", reply.ErrorCode);
    }

    [Fact]
    public async Task AModelWithoutAConfiguredWindowIsAnErrorNamingItNotAGuess()
    {
        var stream = new ScriptedStream().Then(Text("ok"));
        var config = Config() with { Model = new ModelConfig { Id = "bare", BaseUrl = "http://x/v1", MaxOutputTokens = 4096 } };

        var events = await Collect(Run(NewContext(), stream, config));

        Assert.Equal(0, stream.Calls);
        var reply = events.OfType<MessageEnd>().Select(e => e.Message).OfType<AssistantMessage>().Last();
        Assert.Contains("'bare'", reply.ErrorMessage);
        Assert.Contains("contextWindow", reply.ErrorMessage);
    }
}

// pi-sharp-checkpoints-7: the AfterSuccessfulReply hook fires once at the end of a successful turn,
// not on error/aborted turns, not while tool calls are pending, and must never fail the run.
public class AfterSuccessfulReplyHookTests
{
    private static AgentContext NewContext(params IAgentTool[] tools) => new()
    {
        SystemPrompt = "system",
        Tools = [.. tools],
    };

    [Fact]
    public async Task FiresOnce_AfterASuccessfulTurnWithToolCalls()
    {
        var tool = new DelegateTool { Execute = (_, _, _, _) => Task.FromResult(AgentToolResult.FromText("ok")) };
        var ctx = NewContext(tool);
        var stream = new ScriptedStream().Then(ToolCalls(Call("c1", "test_tool", Json("{}")))).Then(Text("done"));
        var calls = new List<(StopReason Stop, int ToolCallCount)>();
        var config = Config() with
        {
            AfterSuccessfulReply = (turn, _) =>
            {
                calls.Add((turn.Message.StopReason, turn.Message.ToolCalls.Count()));
                return ValueTask.FromResult<IReadOnlyList<PiSharp.Core.Session.CheckpointEntry>?>(null);
            },
        };

        var events = await Collect(AgentLoop.Run([new UserMessage("hello")], ctx, config, stream.Stream));

        Assert.IsType<AgentEnd>(events[^1]);
        var fire = Assert.Single(calls);
        Assert.Equal(StopReason.Stop, fire.Stop);
        // The reply turn is the text-only turn; its own tool call list is empty. The batches being
        // closed (the mutating batch of the run) are remembered by the hook's closure, not the turn.
        Assert.Equal(0, fire.ToolCallCount);
    }

    [Fact]
    public async Task DoesNotFire_WhenTheReplyTurnItselfHasPendingToolCalls()
    {
        var tool = new DelegateTool { Execute = (_, _, _, _) => Task.FromResult(AgentToolResult.FromText("ok")) };
        var ctx = NewContext(tool);
        // The stream is exhausted by the first turn's tool batch: the final reply comes from the
        // exhausted-stream error path, never as a clean TurnEnd. The only TurnEnd is the tool batch's.
        var stream = new ScriptedStream().Then(ToolCalls(Call("c1", "test_tool", Json("{}"))));
        var fires = 0;
        var config = Config() with
        {
            AfterSuccessfulReply = (_, _) => { fires++; return ValueTask.FromResult<IReadOnlyList<PiSharp.Core.Session.CheckpointEntry>?>(null); },
        };

        await Collect(AgentLoop.Run([new UserMessage("hello")], ctx, config, stream.Stream));
        Assert.Equal(0, fires);
    }

    [Fact]
    public async Task DoesNotFire_OnAnErrorStop()
    {
        var ctx = NewContext();
        var stream = new ScriptedStream().Then(Error("provider unavailable"));
        var fires = 0;
        var config = Config() with
        {
            AfterSuccessfulReply = (_, _) => { fires++; return ValueTask.FromResult<IReadOnlyList<PiSharp.Core.Session.CheckpointEntry>?>(null); },
        };

        await Collect(AgentLoop.Run([new UserMessage("hello")], ctx, config, stream.Stream));
        Assert.Equal(0, fires);
    }

    [Fact]
    public async Task DoesNotFire_OnAnAbortedStop()
    {
        var ctx = NewContext();
        var stream = new ScriptedStream().Then(
            Fakes.Assistant(StopReason.Aborted) with { ErrorMessage = "Operation aborted" });
        var fires = 0;
        var config = Config() with
        {
            AfterSuccessfulReply = (_, _) => { fires++; return ValueTask.FromResult<IReadOnlyList<PiSharp.Core.Session.CheckpointEntry>?>(null); },
        };

        await Collect(AgentLoop.Run([new UserMessage("hello")], ctx, config, stream.Stream));
        Assert.Equal(0, fires);
    }

    [Fact]
    public async Task DoesNotFire_WhenTheStopHookBlocks()
    {
        var ctx = NewContext();
        var stream = new ScriptedStream().Then(Text("one")).Then(Text("two"));
        var runner = new CommandHookRunner(
            new Dictionary<CommandHookEvent, CommandHook>
            {
                [CommandHookEvent.Stop] = new CommandHook("block", "v1", TimeSpan.FromSeconds(5)),
            });
        var fires = 0;
        var config = Config() with
        {
            CommandHooks = runner,
            AfterSuccessfulReply = (_, _) => { fires++; return ValueTask.FromResult<IReadOnlyList<PiSharp.Core.Session.CheckpointEntry>?>(null); },
        };

        var events = await Collect(AgentLoop.Run([new UserMessage("hello")], ctx, config, stream.Stream));

        // The blocked turn is not a successful reply; the unblocked continuation is.
        Assert.Equal(1, fires);
        Assert.IsType<AgentEnd>(events[^1]);
    }

    [Fact]
    public async Task AThrowingHookDoesNotFailTheRun()
    {
        var ctx = NewContext();
        var stream = new ScriptedStream().Then(Text("ok"));
        var config = Config() with
        {
            AfterSuccessfulReply = (_, _) => throw new InvalidOperationException("snapshot boom"),
        };

        var events = await Collect(AgentLoop.Run([new UserMessage("hello")], ctx, config, stream.Stream));
        Assert.IsType<AgentEnd>(events[^1]);
        Assert.Equal("ok", ((AssistantMessage)ctx.Messages[^1]).Text);
    }

    [Fact]
    public async Task HookRuns_BeforeShouldStopAfterTurn()
    {
        var ctx = NewContext();
        var stream = new ScriptedStream().Then(Text("ok"));
        var order = new List<string>();
        var config = Config() with
        {
            AfterSuccessfulReply = (_, _) =>
            {
                order.Add("after-reply");
                return ValueTask.FromResult<IReadOnlyList<PiSharp.Core.Session.CheckpointEntry>?>(null);
            },
            ShouldStopAfterTurn = _ =>
            {
                order.Add("should-stop");
                return ValueTask.FromResult(true);
            },
        };

        var events = await Collect(AgentLoop.Run([new UserMessage("hello")], ctx, config, stream.Stream));

        Assert.Equal(["after-reply", "should-stop"], order);
        Assert.IsType<AgentEnd>(events[^1]);
    }
}
