using PiSharp.Core.Messages;
using PiSharp.Core.Skills;
using PiSharp.Tests.Mcp;
using PiSharp.Tests.Tools;

namespace PiSharp.Tests.Skills;

public class PromptTemplateTests
{
    [Fact]
    public void ExpandLocalSubstitutesArgumentsAndNumbered()
    {
        var body = "Review $1 of $ARGUMENTS";
        Assert.Equal("Review 12 of 12 34", PromptTemplate.ExpandLocal(body, "12 34"));
        Assert.Equal("Review  of ", PromptTemplate.ExpandLocal("Review $1 of $2", ""));
    }

    [Fact]
    public void LoadLocalReadsFrontmatter()
    {
        using var tmp = new TempWorkspace();
        tmp.File(".pi-sharp/prompts/review.md", """
            ---
            description: Review a PR
            argument-hint: <url>
            ---
            Look at $ARGUMENTS
            """);

        var templates = PromptTemplateLoader.LoadLocal(tmp.Path, tmp.Dir("empty-global"));
        var t = Assert.Single(templates);
        Assert.Equal("review", t.Name);
        Assert.Equal("Review a PR", t.Description);
        Assert.Equal("<url>", t.ArgumentHint);
        Assert.Equal(PromptSourceKind.Local, t.Source);
    }

    [Fact]
    public async Task McpPromptExpandsViaPromptsGet()
    {
        using var tmp = new TempWorkspace();
        McpClientTests.SeedStore(tmp);
        await using var session = await McpClientTests.ConnectStore(tmp.Path);

        var t = new PromptTemplate
        {
            Name = "store:review",
            Source = PromptSourceKind.Mcp,
            ServerAlias = "store",
            Client = session.Client,
            Arguments = [new PiSharp.Core.Mcp.McpPromptArgument("ARGUMENTS", null, false)],
        };
        var messages = await t.ExpandAsync("PR-42");
        var user = Assert.IsType<UserMessage>(Assert.Single(messages));
        Assert.Contains("PR-42", user.Text);
    }
}
