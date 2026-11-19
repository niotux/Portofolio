using PiSharp.Core.Mcp;
using PiSharp.Core.Messages;
using PiSharp.Tests.Tools;

namespace PiSharp.Tests.Mcp;

public class McpContentMappingTests
{
    [Fact]
    public void TextPartMapsToTextContent()
    {
        var parts = McpAgentTool.MapContent([new McpTextPart("hello")]);

        var text = Assert.IsType<TextContent>(Assert.Single(parts));
        Assert.Equal("hello", text.Text);
    }

    [Fact]
    public void ImagePartMapsToImageContent()
    {
        var parts = McpAgentTool.MapContent([new McpImagePart(ImageFixtures.Png().Data, "image/png")]);

        var image = Assert.IsType<ImageContent>(Assert.Single(parts));
        Assert.Equal(ImageFixtures.Png().Data, image.Data);
        Assert.Equal("image/png", image.MimeType);
    }

    [Fact]
    public void ResourceWithTextMapsToTextContent()
    {
        var resource = new McpResourceContents("res://doc", "text/markdown", "doc text", null);
        var parts = McpAgentTool.MapContent([new McpResourcePart(resource)]);

        var text = Assert.IsType<TextContent>(Assert.Single(parts));
        Assert.Equal("doc text", text.Text);
    }

    [Fact]
    public void ResourceWithImageBlobMapsToImageContent()
    {
        var resource = new McpResourceContents("res://img", "image/png", null, ImageFixtures.Png().Data);
        var parts = McpAgentTool.MapContent([new McpResourcePart(resource)]);

        var image = Assert.IsType<ImageContent>(Assert.Single(parts));
        Assert.Equal(ImageFixtures.Png().Data, image.Data);
        Assert.Equal("image/png", image.MimeType);
    }

    [Fact]
    public void ResourceWithPdfBlobMapsToResourcePlaceholder()
    {
        var resource = new McpResourceContents("res://doc.pdf", "application/pdf", null, "BASE64");
        var parts = McpAgentTool.MapContent([new McpResourcePart(resource)]);

        var text = Assert.IsType<TextContent>(Assert.Single(parts));
        Assert.Equal("[resource res://doc.pdf]", text.Text);
    }

    [Fact]
    public void ResourceWithBlobAndNullMimeTypeMapsToResourcePlaceholder()
    {
        var resource = new McpResourceContents("res://data", null, null, "BASE64");
        var parts = McpAgentTool.MapContent([new McpResourcePart(resource)]);

        var text = Assert.IsType<TextContent>(Assert.Single(parts));
        Assert.Equal("[resource res://data]", text.Text);
    }

    [Fact]
    public void ResourceLinkMapsToResourcePlaceholder()
    {
        var parts = McpAgentTool.MapContent([new McpResourceLinkPart("res://linked", "Linked", "text/plain")]);

        var text = Assert.IsType<TextContent>(Assert.Single(parts));
        Assert.Equal("[resource res://linked]", text.Text);
    }

    [Fact]
    public void MixedPartsPreserveOrderAndTypes()
    {
        var parts = McpAgentTool.MapContent(
        [
            new McpTextPart("t"),
            new McpImagePart(ImageFixtures.Png().Data, "image/png"),
            new McpResourcePart(new McpResourceContents("res://x", "text/plain", "doc", null)),
        ]);

        Assert.Equal(3, parts.Count);
        Assert.Equal("t", Assert.IsType<TextContent>(parts[0]).Text);
        var image = Assert.IsType<ImageContent>(parts[1]);
        Assert.Equal(ImageFixtures.Png().Data, image.Data);
        Assert.Equal("image/png", image.MimeType);
        Assert.Equal("doc", Assert.IsType<TextContent>(parts[2]).Text);
    }

    [Fact]
    public void EmptyListMapsToEmptyResult()
    {
        var parts = McpAgentTool.MapContent(Array.Empty<McpContentPart>());

        Assert.Empty(parts);
    }
}
