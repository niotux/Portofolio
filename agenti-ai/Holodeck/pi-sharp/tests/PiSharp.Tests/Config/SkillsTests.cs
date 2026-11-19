using PiSharp.Core.Skills;
using PiSharp.Tests.Tools;

namespace PiSharp.Tests.Configuration;

public class SkillsTests
{
    [Fact]
    public void LoadsSkillMdAndFormatsPrompt()
    {
        using var tmp = new TempWorkspace();
        var cwd = tmp.Path;
        var skillDir = tmp.Dir(".pi-sharp/skills/code-review");
        File.WriteAllText(Path.Combine(skillDir, "SKILL.md"), """
            ---
            name: code-review
            description: Review diffs carefully
            ---
            Read the diff.
            """);

        var result = SkillLoader.Load(cwd, globalDir: tmp.Dir("empty-global"));
        var skill = Assert.Single(result.Skills);
        Assert.Equal("code-review", skill.Name);
        Assert.Equal("Review diffs carefully", skill.Description);
        Assert.Equal("project", skill.DiscoverySource);

        var formatted = SkillLoader.FormatForPrompt(result.Skills);
        Assert.Contains("<available_skills>", formatted);
        Assert.Contains("<name>code-review</name>", formatted);
        Assert.Contains("Use the read tool", formatted);
    }

    [Fact]
    public void DiscoversAgentsSkillsInAncestors()
    {
        using var tmp = new TempWorkspace();
        var root = tmp.Dir("repo");
        var nested = tmp.Dir("repo/src/app");
        var skillDir = Path.Combine(root, ".agents", "skills", "foo");
        Directory.CreateDirectory(skillDir);
        File.WriteAllText(Path.Combine(skillDir, "SKILL.md"), """
            ---
            name: foo
            description: Foo skill
            ---
            """);

        var result = SkillLoader.Load(nested, globalDir: tmp.Dir("empty-global"));
        Assert.Contains(result.Skills, s => s.Name == "foo" && s.DiscoverySource == "agents");
    }

    [Fact]
    public void FirstNameWinsOnCollision()
    {
        using var tmp = new TempWorkspace();
        var global = tmp.Dir("global");
        var gSkill = Path.Combine(global, "skills", "dup");
        Directory.CreateDirectory(gSkill);
        File.WriteAllText(Path.Combine(gSkill, "SKILL.md"), "---\nname: dup\ndescription: global one\n---\n");
        var pSkill = tmp.Dir("proj/.pi-sharp/skills/dup");
        File.WriteAllText(Path.Combine(pSkill, "SKILL.md"), "---\nname: dup\ndescription: project one\n---\n");

        var result = SkillLoader.Load(Path.Combine(tmp.Path, "proj"), global);
        var skill = Assert.Single(result.Skills);
        Assert.Equal("global one", skill.Description);
        Assert.Contains(result.Diagnostics, d => d.Type == "collision");
    }

    [Fact]
    public void HiddenSkillsStayOutOfPrompt()
    {
        var hidden = new Skill("x", "hid", SkillSourceKind.Local, "/x") { DisableModelInvocation = true };
        Assert.Equal("", SkillLoader.FormatForPrompt([hidden]));
    }

    [Fact]
    public void RemoteSkillsCanBeCappedWhileLocalSkillsRemainVisible()
    {
        var skills = new[]
        {
            new Skill("local", "Local", SkillSourceKind.Local, "/local"),
            new Skill("remote-one", "Remote one", SkillSourceKind.Mcp, "skill://store/remote-one/SKILL.md", "store"),
            new Skill("remote-two", "Remote two", SkillSourceKind.Mcp, "skill://store/remote-two/SKILL.md", "store"),
        };

        var formatted = SkillLoader.FormatForPrompt(skills, remoteSkillCap: 1);

        Assert.Contains("<name>local</name>", formatted);
        Assert.Contains("<name>remote-one</name>", formatted);
        Assert.DoesNotContain("<name>remote-two</name>", formatted);
        Assert.Contains("There are 1 additional remote skills", formatted);
    }
}
