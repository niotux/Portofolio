using PiSharp.Core.Mcp;
using PiSharp.Core.Skills;
using PiSharp.Tests.Mcp;
using PiSharp.Tests.Tools;

namespace PiSharp.Tests.Skills;

public class SkillLoaderTests
{
    [Fact]
    public void LoadsLocalSkillsAndGlobalWinsOnCollision()
    {
        using var tmp = new TempWorkspace();
        var global = tmp.Dir("global");
        var cwd = tmp.Dir("proj");
        Directory.CreateDirectory(Path.Combine(global, "skills", "demo"));
        File.WriteAllText(Path.Combine(global, "skills", "demo", "SKILL.md"), """
            ---
            name: demo
            description: global demo
            ---
            g
            """);
        Directory.CreateDirectory(Path.Combine(cwd, ".pi-sharp", "skills", "demo"));
        File.WriteAllText(Path.Combine(cwd, ".pi-sharp", "skills", "demo", "SKILL.md"), """
            ---
            name: demo
            description: project demo
            ---
            p
            """);
        Directory.CreateDirectory(Path.Combine(cwd, ".agents", "skills", "other"));
        File.WriteAllText(Path.Combine(cwd, ".agents", "skills", "other", "SKILL.md"), """
            ---
            name: other
            description: from agents
            ---
            o
            """);

        var skills = SkillLoader.LoadLocal(cwd, global);
        Assert.Equal("global demo", skills.Single(s => s.Name == "demo").Description);
        Assert.Contains(skills, s => s.Name == "other");
    }

    [Fact]
    public async Task IndexPrefersLocalOverMcpWithSameName()
    {
        using var tmp = new TempWorkspace();
        McpClientTests.SeedStore(tmp);
        tmp.File(".pi-sharp/skills/demo/SKILL.md", """
            ---
            name: demo
            description: local wins
            ---
            local
            """);
        await using var session = await McpClientTests.ConnectStore(tmp.Path);
        var mcp = new McpManager();
        // Reuse the already-connected client by listing resources through it.
        var remote = SkillLoader.FromMcpResources(
            (await session.Client.ListResourcesAsync()).Select(r => ("store", r)));
        var local = SkillLoader.LoadLocal(tmp.Path, tmp.Dir("empty-global"));
        var index = new SkillIndex(local, remote);

        var demo = index.Find("demo")!;
        Assert.Equal(SkillSourceKind.Local, demo.Source);
        Assert.Equal("local wins", demo.Description);
        Assert.Contains("demo", index.FormatForSystemPrompt());
    }

    [Fact]
    public void SkillNameFromUri()
    {
        Assert.Equal("demo", SkillLoader.SkillNameFromUri("skill://store/demo/SKILL.md"));
        Assert.Equal("demo", SkillLoader.SkillNameFromUri("skill://store/demo/scripts/run.sh"));
    }
}
