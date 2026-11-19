using PiSharp.Core.Config;
using PiSharp.Tests.Tools;

namespace PiSharp.Tests.Configuration;

[Collection("cubicle-env")]
public class FactorySettingsTests
{
    [Fact]
    public void ParsesFactorySection()
    {
        var s = SettingsLoader.Parse("""
            {"factory":{"kbUrl":"http://kb:8081/","chatUrl":"http://chat:8082/","actor":"coder-06-pi1","token":"factory-token","idleExitSeconds":900,"correctionFrom":["team-lead-2","product-owner-2"]}}
            """);
        Assert.NotNull(s.Factory);
        Assert.Equal("http://kb:8081/", s.Factory.KbUrl);
        Assert.Equal("http://chat:8082/", s.Factory.ChatUrl);
        Assert.Equal("coder-06-pi1", s.Factory.Actor);
        Assert.Equal("factory-token", s.Factory.Token);
        Assert.Equal(900, s.Factory.IdleExitSeconds);
        Assert.Equal(new[] { "team-lead-2", "product-owner-2" }, s.Factory.CorrectionFrom);
        Assert.True(s.Factory.Attached);
    }

    [Fact]
    public void MissingSectionMeansNull()
    {
        Assert.Null(SettingsLoader.Parse("""{"defaultModel":"x"}""").Factory);
        Assert.Null(SettingsLoader.Parse("{}").Factory);
    }

    [Fact]
    public void AttachedNeedsAnActor()
    {
        Assert.False(new FactorySettings().Attached);
        Assert.False(new FactorySettings { KbUrl = "http://kb", Actor = "   " }.Attached);
        Assert.False(SettingsLoader.Parse("""{"factory":{"kbUrl":"http://kb"}}""").Factory!.Attached);
        Assert.True(new FactorySettings { Actor = "coder-06-pi1" }.Attached);
    }

    [Fact]
    public void UnknownKeysAreIgnored()
    {
        var s = SettingsLoader.Parse("""
            {"factory":{"actor":"coder-06-pi1","nope":"x","rooms":["board"]}}
            """);
        Assert.NotNull(s.Factory);
        Assert.Equal("coder-06-pi1", s.Factory.Actor);
        Assert.Null(s.Factory.KbUrl);
        Assert.Null(s.Factory.ChatUrl);
        Assert.Null(s.Factory.Token);
        Assert.Null(s.Factory.IdleExitSeconds);
    }

    [Fact]
    public void IdleExitSecondsMustBeANumber()
    {
        Assert.Equal(60, SettingsLoader.Parse("""{"factory":{"idleExitSeconds":60}}""").Factory!.IdleExitSeconds);
        Assert.Null(SettingsLoader.Parse("""{"factory":{"idleExitSeconds":"60"}}""").Factory!.IdleExitSeconds);
    }

    [Fact]
    public void MergeIsHigherWinsPerKey()
    {
        var lower = new FactorySettings
        {
            KbUrl = "http://kb-global",
            ChatUrl = "http://chat-global",
            Actor = "coder-06-pi1",
            Token = "global-token",
            IdleExitSeconds = 300,
        };
        var higher = new FactorySettings { ChatUrl = "http://chat-project", Actor = "coder-06-pi2" };

        var merged = FactorySettings.Merge(lower, higher);
        Assert.NotNull(merged);
        Assert.Equal("http://kb-global", merged.KbUrl);
        Assert.Equal("http://chat-project", merged.ChatUrl);
        Assert.Equal("coder-06-pi2", merged.Actor);
        Assert.Equal("global-token", merged.Token);
        Assert.Equal(300, merged.IdleExitSeconds);
    }

    [Fact]
    public void MergeWithOneSideNullKeepsTheOther()
    {
        var only = new FactorySettings { Actor = "coder-06-pi1" };
        Assert.Null(FactorySettings.Merge(null, null));
        Assert.Same(only, FactorySettings.Merge(null, only));
        Assert.Same(only, FactorySettings.Merge(only, null));
    }

    [Fact]
    public void ProjectOverlayOverridesGlobal()
    {
        using var tmp = new TempWorkspace();
        var global = tmp.Dir("global");
        var cwd = tmp.Dir("proj");
        tmp.File("global/settings.json", """
            {"factory":{"kbUrl":"http://kb-global","chatUrl":"http://chat-global","actor":"coder-06-pi1","idleExitSeconds":300}}
            """);
        tmp.File("proj/.pi-sharp/settings.json", """{"factory":{"chatUrl":"http://chat-project"}}""");

        var s = SettingsLoader.Load(cwd, global);
        Assert.NotNull(s.Factory);
        Assert.Equal("http://kb-global", s.Factory.KbUrl);
        Assert.Equal("http://chat-project", s.Factory.ChatUrl);
        Assert.Equal("coder-06-pi1", s.Factory.Actor);
        Assert.Equal(300, s.Factory.IdleExitSeconds);
    }

    [Fact]
    public void TokenFallsBackToEnvironment()
    {
        var value = "env-" + Guid.NewGuid().ToString("N");
        var settings = new FactorySettings { Actor = "coder-06-pi1" };
        var previous = Environment.GetEnvironmentVariable(FactorySettings.TokenEnv);
        Environment.SetEnvironmentVariable(FactorySettings.TokenEnv, null);
        try
        {
            Assert.Null(settings.ResolveToken());
            Assert.Equal("factory-token", (settings with { Token = "factory-token" }).ResolveToken());
            Environment.SetEnvironmentVariable(FactorySettings.TokenEnv, value);
            Assert.Equal(value, settings.ResolveToken());
            Assert.Equal("factory-token", (settings with { Token = "factory-token" }).ResolveToken());
        }
        finally
        {
            Environment.SetEnvironmentVariable(FactorySettings.TokenEnv, previous);
        }
    }
}
