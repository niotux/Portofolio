using PiSharp.Core.Config;
using PiSharp.Core.Skills;
using PiSharp.Tests.Tools;

namespace PiSharp.Tests.Configuration;

public class SystemPromptTests
{
    [Fact]
    public void IncludesToolsContextSkillsAndCwd()
    {
        var prompt = SystemPrompt.Build(new SystemPromptOptions
        {
            Cwd = "/work/proj",
            SelectedTools = ["read", "bash", "edit", "write"],
            ToolSnippets = new Dictionary<string, string> { ["read"] = "Read a file.", ["bash"] = "Run a command." },
            ContextFiles = [new ContextFile("/work/proj/AGENTS.md", "Use spaces.")],
            Skills = [new Skill("foo", "Foo things", SkillSourceKind.Local, "/s")],
        });
        Assert.Contains("pi-sharp", prompt);
        Assert.Contains("- read: Read a file.", prompt);
        Assert.Contains("<project_instructions path=\"/work/proj/AGENTS.md\">", prompt);
        Assert.Contains("Use spaces.", prompt);
        Assert.Contains("<name>foo</name>", prompt);
        Assert.Contains("Current working directory: /work/proj", prompt);
        Assert.DoesNotContain("Pi documentation", prompt);
    }

    [Fact]
    public void CustomPromptReplacesDefault()
    {
        var prompt = SystemPrompt.Build(new SystemPromptOptions
        {
            Cwd = "/tmp",
            CustomPrompt = "You are a test bot.",
            AppendSystemPrompt = "Be brief.",
        });
        Assert.StartsWith("You are a test bot.", prompt);
        Assert.Contains("Be brief.", prompt);
        Assert.DoesNotContain("Available tools:", prompt);
    }

    [Fact]
    public void SearchHintAndRemoteSkillCapAreIncluded()
    {
        var skills = new[]
        {
            new Skill("local", "Local skill", SkillSourceKind.Local, "/local"),
            new Skill("remote-one", "First remote skill", SkillSourceKind.Mcp, "skill://store/remote-one/SKILL.md", "store"),
            new Skill("remote-two", "Second remote skill", SkillSourceKind.Mcp, "skill://store/remote-two/SKILL.md", "store"),
        };

        var prompt = SystemPrompt.Build(new SystemPromptOptions
        {
            Cwd = "/work",
            SelectedTools = ["read", "store__search_skills", "store__install_skill"],
            ToolSnippets = new Dictionary<string, string>
            {
                ["read"] = "Read a file.",
                ["store__search_skills"] = "Search skills.",
                ["store__install_skill"] = "Install a skill.",
            },
            Skills = skills,
            RemoteSkillsPromptCap = 1,
        });

        Assert.Contains("If no listed skill fits", prompt);
        Assert.Contains("<name>local</name>", prompt);
        Assert.Contains("<name>remote-one</name>", prompt);
        Assert.DoesNotContain("<name>remote-two</name>", prompt);
        Assert.Contains("There are 1 additional remote skills", prompt);
    }

    [Fact]
    public void LoadsAgentsMdFromCwdAndParents()
    {
        using var tmp = new TempWorkspace();
        var root = tmp.Dir("repo");
        var nested = tmp.Dir("repo/src");
        File.WriteAllText(Path.Combine(root, "AGENTS.md"), "root rules");
        File.WriteAllText(Path.Combine(nested, "CLAUDE.md"), "nested rules");

        var files = ContextFiles.Load(nested, globalDir: tmp.Dir("empty-global"));
        Assert.Equal(2, files.Count);
        Assert.Contains(files, f => f.Content == "root rules");
        Assert.Contains(files, f => f.Content == "nested rules");
    }
}
