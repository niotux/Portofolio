using PiSharp.Core.Skills;

namespace PiSharp.Tests.Configuration;

public class FrontmatterTests
{
    [Fact]
    public void ParsesYamlScalarsAndBody()
    {
        var doc = Frontmatter.Parse("""
            ---
            name: review
            description: "Review a PR"
            argument-hint: '<url>'
            disable-model-invocation: true
            ---
            Body $1
            """);
        Assert.Equal("review", doc.Get("name"));
        Assert.Equal("Review a PR", doc.Get("description"));
        Assert.Equal("<url>", doc.Get("argument-hint"));
        Assert.True(doc.GetBool("disable-model-invocation"));
        Assert.Equal("Body $1", doc.Body);
    }

    [Fact]
    public void MissingFenceReturnsWholeDocument()
    {
        var doc = Frontmatter.Parse("just text");
        Assert.Empty(doc.Fields);
        Assert.Equal("just text", doc.Body);
    }

    [Fact]
    public void StripsBomAndNormalizesCrlf()
    {
        var doc = Frontmatter.Parse("\uFEFF---\r\nname: x\r\n---\r\nhello");
        Assert.Equal("x", doc.Get("name"));
        Assert.Equal("hello", doc.Body);
    }
}
