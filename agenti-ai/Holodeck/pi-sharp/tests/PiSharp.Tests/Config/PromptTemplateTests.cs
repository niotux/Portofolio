using PiSharp.Core.Skills;
using PiSharp.Tests.Tools;

namespace PiSharp.Tests.Configuration;

public class PromptTemplateTests
{
    [Fact]
    public void LoadsMarkdownAndExpandsArgs()
    {
        using var tmp = new TempWorkspace();
        var cwd = tmp.Dir("proj");
        var prompts = tmp.Dir("proj/.pi-sharp/prompts");
        File.WriteAllText(Path.Combine(prompts, "review.md"), """
            ---
            description: Review a change
            argument-hint: <path>
            ---
            Look at $1 and also $@
            """);

        var templates = PromptTemplateLoader.LoadLocal(cwd, globalDir: tmp.Dir("empty-global"));
        var t = Assert.Single(templates);
        Assert.Equal("review", t.Name);
        Assert.Equal("Review a change", t.Description);
        Assert.Equal("<path>", t.ArgumentHint);

        var expanded = PromptTemplate.Expand("/review src/a.cs extra", templates);
        Assert.Equal("Look at src/a.cs and also src/a.cs extra", expanded);
    }

    [Theory]
    [InlineData("$1", new[] { "a", "b" }, "a")]
    [InlineData("$2", new[] { "a" }, "")]
    [InlineData("$@", new[] { "a", "b" }, "a b")]
    [InlineData("${1:-def}", new string[0], "def")]
    [InlineData("${@:2}", new[] { "a", "b", "c" }, "b c")]
    [InlineData("${@:2:1}", new[] { "a", "b", "c" }, "b")]
    public void SubstituteArgsMatchesPi(string template, string[] args, string expected)
    {
        Assert.Equal(expected, PromptTemplate.SubstituteArgs(template, args));
    }

    [Fact]
    public void ParseCommandArgsHonorsQuotes()
    {
        var args = PromptTemplate.ParseCommandArgs("""one "two three" 'four five'""");
        Assert.Equal(["one", "two three", "four five"], args);
    }
}
