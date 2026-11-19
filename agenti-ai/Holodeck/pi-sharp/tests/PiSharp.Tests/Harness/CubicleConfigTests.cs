using PiSharp.Cli;
using PiSharp.Core.Config;
using PiSharp.Tests.Tools;

namespace PiSharp.Tests.Harness;

[Collection("cubicle-env")]
public class CubicleConfigTests
{
    [Fact]
    public void ParsesCubicleSection()
    {
        var s = SettingsLoader.Parse("""
            {"cubicle":{"url":"http://chat:8082/","token":"tok","followUpWaitSeconds":45,"about":["wi-7","pi-sharp"]}}
            """);
        Assert.NotNull(s.Cubicle);
        Assert.True(s.Cubicle.Enabled);
        Assert.Equal("http://chat:8082/", s.Cubicle.Url);
        Assert.Equal("tok", s.Cubicle.Token);
        Assert.Equal(45, s.Cubicle.FollowUpWaitSeconds);
        Assert.Equal(["wi-7", "pi-sharp"], s.Cubicle.About);
    }

    [Fact]
    public void MissingSectionMeansOff()
    {
        Assert.Null(SettingsLoader.Parse("""{"defaultModel":"x"}""").Cubicle);
        Assert.False(SettingsLoader.Parse("""{"cubicle":{"token":"t"}}""").Cubicle!.Enabled);
    }

    [Fact]
    public void ProjectOverlayOverridesGlobal()
    {
        using var tmp = new TempWorkspace();
        var global = tmp.Dir("global");
        var cwd = tmp.Dir("proj");
        tmp.File("global/settings.json", """{"cubicle":{"url":"http://global","token":"g","about":["a"]}}""");
        tmp.File("proj/.pi-sharp/settings.json", """{"cubicle":{"url":"http://project","about":["b"]}}""");
        var s = SettingsLoader.Load(cwd, global);
        Assert.NotNull(s.Cubicle);
        Assert.Equal("http://project", s.Cubicle.Url);
        Assert.Equal("g", s.Cubicle.Token);
        Assert.Equal(["a", "b"], s.Cubicle.About);
    }

    [Fact]
    public void TokenFallsBackToEnvironment()
    {
        var name = "env-" + Guid.NewGuid().ToString("N");
        var settings = new CubicleSettings { Url = "http://x" };
        // The seat may legitimately carry PI_FACTORY_TOKEN (this is the env a running agent
        // authenticates with), so save it and restore it afterwards, exactly as
        // FactorySettingsTests.TokenFallsBackToEnvironment does. Without the save this test only
        // passes on a seat that happens to have no token configured (pi-sharp-wi-tests-clean-seat-default).
        var previous = Environment.GetEnvironmentVariable(CubicleSettings.TokenEnv);
        Environment.SetEnvironmentVariable(CubicleSettings.TokenEnv, null);
        try
        {
            Assert.Null(settings.ResolveToken());
            Assert.Equal("explicit", (settings with { Token = "explicit" }).ResolveToken());
            Environment.SetEnvironmentVariable(CubicleSettings.TokenEnv, name);
            Assert.Equal(name, settings.ResolveToken());
            Assert.Equal("explicit", (settings with { Token = "explicit" }).ResolveToken());
        }
        finally
        {
            Environment.SetEnvironmentVariable(CubicleSettings.TokenEnv, previous);
        }
    }

    [Fact]
    public void CliFlagsParse()
    {
        var args = ArgsParser.Parse(["--cubicle-url", "http://chat:8082", "--cubicle-token-env=MY_TOKEN", "-p", "hi"]);
        Assert.Empty(args.Diagnostics);
        Assert.Equal("http://chat:8082", args.CubicleUrl);
        Assert.Equal("MY_TOKEN", args.CubicleTokenEnv);
        Assert.Equal(CliMode.Print, args.Mode);
        Assert.Contains("--cubicle-url", ArgsParser.HelpText(), StringComparison.Ordinal);
    }
}
