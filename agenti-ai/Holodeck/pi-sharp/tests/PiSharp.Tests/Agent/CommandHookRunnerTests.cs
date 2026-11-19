using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Config;

namespace PiSharp.Tests.Agent;

public class CommandHookRunnerTests
{
    [Fact]
    public void RuleParserReadsFencedHookBlocksAndIgnoresOtherFences()
    {
        var hooks = CommandHookRuleParser.ParseDocuments(["""
            # Stop check
            ```json
            {"event":"Stop","command":"not-a-hook"}
            ```
            ```hook
            {"event":"Stop","command":"./check-stop","version":"r4","timeoutSeconds":9,"failMode":"closed"}
            ```
            """]);

        var hook = Assert.Single(hooks);
        Assert.Equal(CommandHookEvent.Stop, hook.Event);
        Assert.Equal("./check-stop", hook.Command);
        Assert.Equal("r4", hook.Version);
        Assert.Equal(TimeSpan.FromSeconds(9), hook.Timeout);
        Assert.Equal(HookFailMode.Closed, hook.FailMode);
    }

    [Fact]
    public async Task InvokesConfiguredHookAndParsesDecision()
    {
        if (OperatingSystem.IsWindows()) return;
        var runner = new CommandHookRunner(new Dictionary<CommandHookEvent, CommandHook>
        {
            [CommandHookEvent.PreToolUse] = new("cat >/dev/null; printf '{\"block\":true,\"reason\":\"policy\"}'", "v1", TimeSpan.FromSeconds(2)),
        });

        var result = await runner.InvokeAsync(CommandHookEvent.PreToolUse, JsonNode.Parse("{\"x\":1}")!);

        Assert.True(result.Block);
        Assert.Equal("policy", result.Reason);
    }

    // plan-pi-sharp-codex item 4 (Codex hooks): several handlers per event, a matcher on the tool's name, contexts joined.
    [Fact]
    public async Task SeveralHandlersRunInOrder_TheMatcherPicksByToolName_AnyBlockWins()
    {
        if (OperatingSystem.IsWindows()) return;
        var runner = new CommandHookRunner(new Dictionary<CommandHookEvent, IReadOnlyList<CommandHook>>
        {
            [CommandHookEvent.PostToolUse] =
            [
                new("cat >/dev/null; printf '{\"additional_context\":\"ran the tests? record the run id\"}'", "v1", TimeSpan.FromSeconds(2), Matcher: "^bash$"),
                new("cat >/dev/null; printf '{\"additional_context\":\"every tool\"}'", "v1", TimeSpan.FromSeconds(2)),
                new("cat >/dev/null; printf '{\"block\":true,\"reason\":\"edits need a read first\"}'", "v1", TimeSpan.FromSeconds(2), Matcher: "^(edit|write)$"),
            ],
        });
        var bash = await runner.InvokeAsync(CommandHookEvent.PostToolUse, JsonNode.Parse("{}")!, toolName: "bash");
        Assert.False(bash.Block);
        Assert.Equal("ran the tests? record the run id\nevery tool", bash.AdditionalContext);
        var edit = await runner.InvokeAsync(CommandHookEvent.PostToolUse, JsonNode.Parse("{}")!, toolName: "edit");
        Assert.True(edit.Block);
        Assert.Equal("edits need a read first", edit.Reason);
        Assert.Equal("every tool", edit.AdditionalContext);
        Assert.True(runner.Has(CommandHookEvent.PostToolUse));
        Assert.False(runner.Has(CommandHookEvent.SessionStart));
    }

    [Fact]
    public void ParsersReadTheNewEventsAndAMatcher()
    {
        var hooks = CommandHookRuleParser.ParseDocuments(["""
            ```hook
            {"event":"PostToolUse","command":"./after","matcher":"^bash$"}
            ```
            ```hook
            {"event":"UserPromptSubmit","command":"./inbox"}
            ```
            """]);
        Assert.Equal([CommandHookEvent.PostToolUse, CommandHookEvent.UserPromptSubmit], hooks.Select(h => h.Event));
        Assert.Equal("^bash$", hooks[0].Matcher);
    }

    [Fact]
    public async Task TimeoutIsCountedAndFailModeApplied()
    {
        if (OperatingSystem.IsWindows()) return;
        var runner = new CommandHookRunner(new Dictionary<CommandHookEvent, CommandHook>
        {
            [CommandHookEvent.Stop] = new("sleep 2", "v1", TimeSpan.FromMilliseconds(30), HookFailMode.Closed),
        });

        var result = await runner.InvokeAsync(CommandHookEvent.Stop, JsonNode.Parse("{}")!);

        Assert.True(result.Block);
        Assert.Equal(1, runner.TimeoutCount(CommandHookEvent.Stop));
    }
}
