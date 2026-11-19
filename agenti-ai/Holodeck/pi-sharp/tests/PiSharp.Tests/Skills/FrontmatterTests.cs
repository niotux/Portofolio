using PiSharp.Core.Skills;

namespace PiSharp.Tests.Skills;

public class FrontmatterTests
{
    [Fact]
    public void ParsesFieldsQuotesAndLists()
    {
        var fm = Frontmatter.Parse("""
            ---
            name: demo
            description: "hello: world"
            tags: [a, b, "c d"]
            ---
            body line
            """);

        Assert.Equal("demo", fm["name"]);
        Assert.Equal("hello: world", fm["description"]);
        Assert.Equal(["a", "b", "c d"], fm.List("tags"));
        Assert.Equal("body line", fm.Body.TrimEnd());
    }

    [Fact]
    public void MissingFrontmatterTreatsWholeFileAsBody()
    {
        var fm = Frontmatter.Parse("# just markdown\n");
        Assert.Empty(fm.Fields);
        Assert.Equal("# just markdown\n", fm.Body);
    }

    [Fact]
    public void UnquotedValuesKeepColonsInValue()
    {
        var fm = Frontmatter.Parse("---\nurl: http://x\n---\n");
        Assert.Equal("http://x", fm["url"]);
    }
}
