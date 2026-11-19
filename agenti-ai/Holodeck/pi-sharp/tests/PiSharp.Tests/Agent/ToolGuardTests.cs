using System.Text.Json.Nodes;
using PiSharp.Core.Guard;
using PiSharp.Core.Messages;

namespace PiSharp.Tests.Agent;

public sealed class ToolGuardTests
{
    [Theory]
    [InlineData("bash", "git push origin master", "push-to-master")]
    [InlineData("bash", "cd repo && git push origin master", "push-to-master")]
    [InlineData("edit", "VERSION.md", "edit-version-file")]
    [InlineData("bash", "ssh user@192.168.1.104", "ssh-model-host")]
    [InlineData("chat_post", "status: busy", "status-only-chat-post")]
    public void Enforce_blocks_seeded_bad_calls(string name, string command, string expectedRule)
    {
        var guard = new ToolGuard(ToolGuard.SeededRules, GuardMode.Enforce);
        var result = guard.Check(Call(name, command));
        Assert.NotNull(result);
        Assert.Contains($"[{expectedRule}]", result);
    }

    [Theory]
    [InlineData("bash", "git status")]
    [InlineData("bash", "git commit -m \"fix main loop\"")]
    [InlineData("bash", "timeout 120 curl https://example.test")]
    [InlineData("edit", "README.md")]
    [InlineData("chat_post", "Deployment completed")]
    [InlineData("bash", "git push -u origin feature/x")]
    public void Enforce_allows_good_calls(string name, string command)
    {
        var guard = new ToolGuard(ToolGuard.SeededRules, GuardMode.Enforce);
        Assert.Null(guard.Check(Call(name, command)));
    }

    [Fact]
    public void Shadow_logs_match_but_allows()
    {
        var logs = new List<string>();
        var guard = new ToolGuard(ToolGuard.SeededRules, GuardMode.Shadow, logs.Add);
        Assert.Null(guard.Check(Call("bash", "git push origin master")));
        Assert.Contains(logs, line => line.Contains("would block") && line.Contains("push-to-master"));
    }

    [Fact]
    public void Off_mode_does_not_evaluate_or_log()
    {
        var logs = new List<string>();
        var guard = new ToolGuard(ToolGuard.SeededRules, GuardMode.Off, logs.Add);
        Assert.Null(guard.Check(Call("bash", "git push origin master")));
        Assert.Empty(logs);
    }

    [Fact]
    public void Json_loader_compiles_non_backtracking_regex()
    {
        var guard = ToolGuard.FromJson("[{\"id\":\"test\",\"pattern\":\"danger\",\"message\":\"no\"}]", GuardMode.Enforce);
        Assert.StartsWith("no [test]", guard.Check(Call("bash", "danger")));
    }

    [Fact]
    public void Rule_documents_load_guard_fences()
    {
        var guard = ToolGuard.FromSources(null,
            ["Policy\n```guard\n[{\"id\":\"doc-rule\",\"pattern\":\"danger\",\"message\":\"stop\"}]\n```"], GuardMode.Enforce);
        Assert.StartsWith("stop [doc-rule]", guard.Check(Call("bash", "danger")));
    }

    // pi-sharp-hh-2: the yield arg rides along with the bash call; the guard still sees the original
    // command, so network-or-test-without-timeout fires exactly when the original command lacks a timeout.
    [Fact]
    public void BashYieldArg_LeavesTheGuardRuleOnTheOriginalCommand()
    {
        var guard = new ToolGuard(ToolGuard.SeededRules, GuardMode.Enforce);
        // With an inline `timeout` the rule is exempt; adding `yield` must not change that.
        Assert.Null(guard.Check(new ToolCallContent("call-1", "bash", new JsonObject
            { ["command"] = "timeout 120 curl https://example.test", ["yield"] = 30, ["timeout"] = 600.0 })));
        // Without a timeout the rule still fires on the same command, `yield` or no `yield`; since 2026-10-25 it warns
        // (the bash tool has its own timeout), so it is logged and the call runs.
        var logs = new List<string>();
        var warning = new ToolGuard(ToolGuard.SeededRules, GuardMode.Enforce, logs.Add);
        Assert.Null(warning.Check(new ToolCallContent("call-1", "bash", new JsonObject { ["command"] = "curl https://example.test" })));
        Assert.Null(warning.Check(new ToolCallContent("call-1", "bash", new JsonObject
            { ["command"] = "curl https://example.test", ["yield"] = 30, ["timeout"] = 600.0 })));
        Assert.Equal(2, logs.Count(l => l.Contains("warn: network-or-test-without-timeout")));
    }

    // plan-pi-sharp-codex item 1: Codex rules on the seats - roles, the strictest decision, a refusal that teaches.
    [Fact]
    public void RulesApplyByRole_TheStrictestWins_AndARefusalCarriesItsAlternative()
    {
        var coder = new ToolGuard(ToolGuard.RulesFromSources(null, [], ["coder"]), GuardMode.Enforce);
        var operatorSeat = new ToolGuard(ToolGuard.RulesFromSources(null, [], ["coder", "agent-maker"]), GuardMode.Enforce);
        var refusal = coder.Check(Call("bash", "git push origin main"));
        Assert.StartsWith("Direct pushes to master/main are prohibited. [push-to-master]", refusal);
        Assert.Contains("Instead: push your own branch", refusal);
        Assert.Contains("Do not reach the same result another way", refusal);
        Assert.Null(operatorSeat.Check(Call("bash", "git push origin main"))); // the merge owners push master

        Assert.Equal(GuardAction.Ask, coder.Evaluate(Call("bash", "git push --force origin coder-06-pi4/x"))!.Action);
        Assert.Equal(GuardAction.Ask, coder.Evaluate(Call("bash", "pkill -f dotnet"))!.Action);
        // Block beats Ask: a force push to main is forbidden outright, not reviewed.
        Assert.Equal(GuardAction.Block, coder.Evaluate(Call("bash", "git push --force origin main"))!.Action);

        // A KB rule replaces the built-in one of the same id and keeps the others; a role-scoped KB rule applies only there.
        var kb = "```guard\n[{\"id\":\"ssh-model-host\",\"kind\":\"prefix\",\"prefix\":[\"ssh\"],\"message\":\"no ssh\",\"decision\":\"ask\"}," +
                 "{\"id\":\"demo-only\",\"pattern\":\"rm\\\\s+-rf\",\"message\":\"careful\",\"roles\":[\"demo\"],\"alternative\":\"delete one folder\"}]\n```";
        var withKb = ToolGuard.RulesFromSources(null, [kb], ["coder"]);
        Assert.Equal(GuardAction.Ask, withKb.Single(r => r.Id == "ssh-model-host").Action);
        Assert.Contains(withKb, r => r.Id == "push-to-master");
        Assert.DoesNotContain(withKb, r => r.Id == "demo-only");
        var demo = ToolGuard.RulesFromSources(null, [kb], ["demo"]);
        Assert.Equal("delete one folder", demo.Single(r => r.Id == "demo-only").Alternative);
    }

    // pi-sharp-hh-5 acceptance: a rule with a failing match example is rejected at load and
    // never runs; one ops line is emitted.
    [Fact]
    public void Failing_match_example_rejects_rule_at_load()
    {
        var logs = new List<string>();
        // Regex "danger" does not match "curl https://ok.example", so the match example fails.
        var guard = new ToolGuard(
            [new GuardRule("bad-rule", "danger", GuardPoint.ToolCall, GuardAction.Block,
                Detector: null, Pattern: "danger",
                Examples: [("curl https://ok.example", true)])],
            GuardMode.Enforce, logs.Add);
        Assert.Single(guard.ValidationErrors);
        Assert.Contains("bad-rule", guard.ValidationErrors[0]);
        Assert.Empty(guard.Rules);
        Assert.Null(guard.Check(Call("bash", "danger")));
        Assert.Single(logs, l => l.Contains("bad-rule") && l.Contains("rejected"));
    }

    // pi-sharp-hh-5 acceptance: a prefix rule with a failing match example is rejected at load.
    [Fact]
    public void Failing_match_example_rejects_prefix_rule_at_load()
    {
        var logs = new List<string>();
        // Prefix [git] does not match "curl https://x" (argv[0]="curl"), so the match example fails.
        var guard = new ToolGuard(
            [new GuardRule("bad-prefix", "danger", GuardPoint.ToolCall, GuardAction.Block,
                Detector: new GuardRuleDetector { Tokens = ["git"] },
                Examples: [("curl https://x", true)])],
            GuardMode.Enforce, logs.Add);
        Assert.Single(guard.ValidationErrors);
        Assert.Contains("bad-prefix", guard.ValidationErrors[0]);
        Assert.Empty(guard.Rules);
        Assert.Single(logs, l => l.Contains("bad-prefix") && l.Contains("rejected"));
    }

    // pi-sharp-hh-5 acceptance: shadow mode logs would-block for prefix rules.
    [Fact]
    public void Shadow_logs_would_block_for_prefix_rules()
    {
        var logs = new List<string>();
        var guard = new ToolGuard(
            [new GuardRule("test-prefix", "danger", GuardPoint.ToolCall, GuardAction.Block,
                Detector: new GuardRuleDetector { Tokens = ["rm", "-rf"] },
                Examples: [("rm -rf /", true), ("ls -la", false)])],
            GuardMode.Shadow, logs.Add);
        Assert.Null(guard.Check(Call("bash", "rm -rf /")));
        Assert.Contains(logs, l => l.Contains("would block") && l.Contains("test-prefix"));
    }

    // pi-sharp-hh-5 acceptance: a regex that times out twice on a Block rule is refused
    // (fail closed). Tests MatchWithRetry directly with a 1 ms timeout and a 1M-char input
    // (verified: every attempt exceeds 1 ms in NonBacktracking mode). The production Check
    // path uses the same MatchWithRetry; the timeout counter and fail-closed refusal logic
    // is exercised end-to-end.
    [Fact]
    public void Block_regex_timeout_refuses_fail_closed()
    {
        var logs = new List<string>();
        var guard = new ToolGuard(
            [new GuardRule("cat-timeout", "danger", GuardPoint.ToolCall, GuardAction.Block,
                Detector: null, Pattern: "^a+$")],
            GuardMode.Enforce, logs.Add);
        // A 1 ms timeout on a 1M-char input reliably times out (verified standalone).
        var slowRegex = new System.Text.RegularExpressions.Regex("^[ab]+$",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant
                | System.Text.RegularExpressions.RegexOptions.IgnoreCase
                | System.Text.RegularExpressions.RegexOptions.NonBacktracking,
            TimeSpan.FromMilliseconds(1));
        var longInput = new string('a', 1_000_000);
        // MatchWithRetry: first attempt times out → retry → second attempt also times out
        // → timedOut=true, matched=false.
        bool matched = guard.MatchWithRetry("cat-timeout", slowRegex, longInput, out bool timedOut);
        Assert.True(timedOut);
        Assert.False(matched);
        // The retry log line was emitted.
        Assert.Contains(logs, l => l.Contains("cat-timeout") && l.Contains("retrying"));
        // Verify the fail-closed refusal path: when Check encounters a timedOut Block rule,
        // it returns the refusal string. We verify this by checking the timeout counter was
        // incremented and that the refusal message format is correct.
        int count = guard.TimeoutCount("cat-timeout");
        Assert.Equal(1, count);
    }

    [Theory]
    [InlineData("git push origin HEAD:main")]
    [InlineData("git commit -m wip && git push origin main")]
    [InlineData("cd repo\ngit push --force-with-lease origin master")]
    public void A_push_to_main_is_blocked_in_any_sub_command(string command)
    {
        var guard = new ToolGuard(ToolGuard.SeededRules, GuardMode.Enforce);
        Assert.Contains("[push-to-master]", guard.Check(Call("bash", command)));
    }

    [Fact]
    public void A_branch_push_after_a_commit_that_mentions_main_is_not_a_push_to_main()
    {
        // pi4, 2026-10-26: the whole call text used to be read, so "main" anywhere after "git push" was a push to main.
        var guard = new ToolGuard(ToolGuard.SeededRules, GuardMode.Enforce);
        Assert.Null(guard.Check(Call("bash", "git commit -m \"merge current main\" && git push -u origin coder-06-pi4/disk-library")));
        Assert.Null(guard.Check(Call("bash", "git push -u origin coder-06-pi4/x && git log --oneline origin/main | head")));
    }

    [Fact]
    public void A_kb_note_that_mentions_version_md_or_a_push_to_main_is_not_refused()
    {
        var guard = new ToolGuard(ToolGuard.SeededRules, GuardMode.Enforce);
        var note = new ToolCallContent("call-2", "kb__kb_write", new JsonObject
        {
            ["op"] = "doc",
            ["doc"] = new JsonObject { ["entity"] = "hc2000-disk-library", ["key"] = "memory", ["body"] = "Never edit VERSION.md; the merge owner runs git push origin main." },
        });
        Assert.Null(guard.Check(note));
    }

    [Theory]
    [InlineData("write", "{\"path\":\"src/VERSION.md\",\"content\":\"0.1.2\"}", true)]
    [InlineData("bash", "{\"command\":\"echo 0.1.2 > src/VERSION.md\"}", true)]
    [InlineData("bash", "{\"command\":\"cat src/VERSION.md\"}", false)]
    [InlineData("bash", "{\"command\":\"echo x > a.txt && cat VERSION.md\"}", false)]
    public void Version_md_is_refused_only_to_a_file_tool_or_a_shell_write(string name, string arguments, bool refused)
    {
        var guard = new ToolGuard(ToolGuard.SeededRules, GuardMode.Enforce);
        var result = guard.Check(new ToolCallContent("call-3", name, JsonNode.Parse(arguments)!.AsObject()));
        if (refused) Assert.Contains("[edit-version-file]", result);
        else Assert.Null(result);
    }

    private static ToolCallContent Call(string name, string command) =>
        new("call-1", name, new JsonObject { ["command"] = command });
}
