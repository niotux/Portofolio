using PiSharp.Core.Config;
using PiSharp.Core.Models;
using PiSharp.Tests.Tools;

namespace PiSharp.Tests.Configuration;

public class SettingsTests
{
    [Fact]
    public void ParseReadsKnownKeys()
    {
        var s = SettingsLoader.Parse("""
            {
              "defaultModel": "maxiclaude",
              "verifyReviewer": "backward",
              "defaultThinkingLevel": "low",
              "temperature": 0.2,
              "maxTokens": 1024,
              "toolCallTimeoutSeconds": 4,
              "commandHooks": [{"event":"PreToolUse","command":"hook-check","version":"v3","timeoutSeconds":8,"failMode":"closed"}],
              "remoteSkillsPromptCap": 0,
              "images": { "warnPixels": 1234 },
              "compaction": { "enabled": false, "reserveTokens": 100, "keepRecentTokens": 50 },
              "skills": ["a"],
              "enableSkillCommands": false,
              "hideThinkingBlock": true
            }
            """);
        Assert.Equal("maxiclaude", s.DefaultModel);
        Assert.Equal("backward", s.VerifyReviewer);
        Assert.Equal(ThinkingLevel.Low, s.DefaultThinkingLevel);
        Assert.Equal(0.2, s.Temperature);
        Assert.Equal(1024, s.MaxTokens);
        Assert.Equal(4, s.ToolCallTimeoutSeconds);
        Assert.Equal(0, s.RemoteSkillsPromptCap);
        Assert.Equal(1234, s.ImageWarnPixels);
        Assert.False(s.CompactionIsEnabled);
        Assert.Equal(100, s.CompactionReserveTokens);
        Assert.False(s.SkillCommandsEnabled);
        Assert.True(s.HideThinkingBlock);
        Assert.Equal(["a"], s.Skills);
        var hook = Assert.Single(s.CommandHooks!);
        Assert.Equal(PiSharp.Core.Agent.CommandHookEvent.PreToolUse, hook.Event);
        Assert.Equal("v3", hook.Version);
        Assert.Equal(TimeSpan.FromSeconds(8), hook.Timeout);
        Assert.Equal(PiSharp.Core.Agent.HookFailMode.Closed, hook.FailMode);
    }

    [Fact]
    public void ProjectOverridesGlobal()
    {
        using var tmp = new TempWorkspace();
        var global = tmp.Dir("global");
        var cwd = tmp.Dir("proj");
        Directory.CreateDirectory(Path.Combine(cwd, ".pi-sharp"));
        File.WriteAllText(Path.Combine(global, "settings.json"), """{"defaultModel":"g","temperature":0.1,"toolCallTimeoutSeconds":12}""");
        File.WriteAllText(Path.Combine(cwd, ".pi-sharp", "settings.json"), """{"defaultModel":"p"}""");

        var s = SettingsLoader.Load(cwd, global);
        Assert.Equal("p", s.DefaultModel);
        Assert.Equal(0.1, s.Temperature);
        Assert.Equal(12, s.ToolCallTimeoutSeconds);
    }

    [Fact]
    public void InvalidJsonIsEmptySettings()
    {
        var s = SettingsLoader.Parse("{not json");
        Assert.Null(s.DefaultModel);
        Assert.True(s.CompactionIsEnabled);
    }

    [Fact]
    public void BackwardReviewerIsOffByDefault()
    {
        var off = SettingsLoader.Parse("{}");
        var subscription = new ModelConfig { Id = "subscription", BaseUrl = "http://localhost/v1", Connection = "seat" };
        var local = new ModelConfig { Id = "local", BaseUrl = "http://localhost/v1" };
        Assert.Null(off.VerifyReviewer);
        Assert.False(off.UseBackwardReviewer(subscription));
        Assert.False(SettingsLoader.Merge(new Settings(), new Settings()).UseBackwardReviewer(subscription));
        var backward = SettingsLoader.Parse("{\"verifyReviewer\":\"backward\"}");
        Assert.True(backward.UseBackwardReviewer(subscription));
        Assert.False(backward.UseBackwardReviewer(local));
    }

    [Fact]
    public void Checkpoints_AutoOnTurnEnd_ParsesFromTheCheckpointsObject()
    {
        var on = SettingsLoader.Parse("""{"checkpoints":{"enabled":true,"autoOnTurnEnd":true}}""");
        Assert.True(on.CheckpointsEnabled);
        Assert.True(on.CheckpointsAutoOnTurnEnd);

        var off = SettingsLoader.Parse("""{"checkpoints":{"enabled":true,"autoOnTurnEnd":false}}""");
        Assert.True(off.CheckpointsEnabled);
        Assert.False(off.CheckpointsAutoOnTurnEnd);

        var absent = SettingsLoader.Parse("""{"checkpoints":true}""");
        Assert.True(absent.CheckpointsEnabled);
        Assert.Null(absent.CheckpointsAutoOnTurnEnd);
    }

    [Fact]
    public void Checkpoints_AutoOnTurnEnd_IsProjectLayerOnly()
    {
        using var tmp = new TempWorkspace();
        var global = tmp.Dir("global");
        var cwd = tmp.Dir("proj");
        Directory.CreateDirectory(Path.Combine(cwd, ".pi-sharp"));

        // Global on + project absent: the global value is ignored by design. The merged result
        // carries only the project layer's value (absent here), so it is null, not the global's true.
        File.WriteAllText(Path.Combine(global, "settings.json"), """{"checkpoints":{"enabled":true,"autoOnTurnEnd":true}}""");
        File.WriteAllText(Path.Combine(cwd, ".pi-sharp", "settings.json"), """{"checkpoints":{"enabled":true}}""");
        var s = SettingsLoader.Load(cwd, global);
        Assert.True(s.CheckpointsEnabled);
        Assert.Null(s.CheckpointsAutoOnTurnEnd); // null == the hook's `== true` gate treats it as off


        // Project on + global off: the project value wins.
        File.WriteAllText(Path.Combine(global, "settings.json"), """{"checkpoints":{"enabled":true,"autoOnTurnEnd":false}}""");
        File.WriteAllText(Path.Combine(cwd, ".pi-sharp", "settings.json"), """{"checkpoints":{"autoOnTurnEnd":true}}""");
        s = SettingsLoader.Load(cwd, global);
        Assert.True(s.CheckpointsEnabled);
        Assert.True(s.CheckpointsAutoOnTurnEnd);
    }
}

public class GuardSettingsTests
{
    [Fact]
    public void Guard_settings_keys_parse_with_safe_defaults()
    {
        var off = new Settings();
        Assert.False(off.GuardRequireTestRunBeforeReview == true); // unset = off
        Assert.Equal(0, off.GuardMaxBlocksPerRulePerTask ?? 0); // 0 = unlimited
        Assert.Equal(3, off.GuardCompletionMaxNudges ?? 3);

        var on = SettingsLoader.Parse("""
            {
              "guardRequireTestRunBeforeReview": true,
              "guardMaxBlocksPerRulePerTask": 4,
              "guardCompletionMaxNudges": 5
            }
            """);
        Assert.True(on.GuardRequireTestRunBeforeReview == true);
        Assert.Equal(4, on.GuardMaxBlocksPerRulePerTask);
        Assert.Equal(5, on.GuardCompletionMaxNudges);

        var merged = SettingsLoader.Merge(on, new Settings { GuardMaxBlocksPerRulePerTask = 2 });
        Assert.True(merged.GuardRequireTestRunBeforeReview == true);
        Assert.Equal(2, merged.GuardMaxBlocksPerRulePerTask); // project wins
        Assert.Equal(5, merged.GuardCompletionMaxNudges); // unset in project keeps global
    }
}
