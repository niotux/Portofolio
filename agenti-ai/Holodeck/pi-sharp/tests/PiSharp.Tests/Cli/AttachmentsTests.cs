using PiSharp.Cli;
using PiSharp.Core.Messages;
using PiSharp.Tests.Tools;

namespace PiSharp.Tests.Cli;

public class AttachmentsTests
{
    [Fact]
    public void WrapsTextFiles()
    {
        using var tmp = new TempWorkspace();
        var path = tmp.File("note.md", "hello");
        var processed = Attachments.Process(["note.md"], tmp.Path);
        Assert.Contains("<file name=\"", processed.Text);
        Assert.Contains("hello", processed.Text);
        Assert.Empty(processed.Images);
        var user = Attachments.ToUserMessage("summarize", processed);
        Assert.Contains("summarize", user.Text);
        Assert.Contains("hello", user.Text);
    }

    [Fact]
    public void DetectsPngByMagicBytes()
    {
        using var tmp = new TempWorkspace();
        var png = Convert.FromBase64String(ImageFixtures.Png(1, 1).Data);
        var file = Path.Combine(tmp.Path, "x.png");
        File.WriteAllBytes(file, png);
        var processed = Attachments.Process(["x.png"], tmp.Path);
        var image = Assert.Single(processed.Images);
        Assert.Equal("image/png", image.MimeType);
    }

    [Fact]
    public void MissingFileThrows()
    {
        using var tmp = new TempWorkspace();
        Assert.Throws<FileNotFoundException>(() => Attachments.Process(["nope.txt"], tmp.Path));
    }

    // @adr-pi-sharp-image-size-warning: an oversized @file image warns once, in the text wrapper and
    // in the sink, and the image is still attached.
    [Fact]
    public void OversizedImageFileWarnsInTextAndSinkAndStillAttaches()
    {
        using var tmp = new TempWorkspace();
        var file = Path.Combine(tmp.Path, "big.png");
        File.WriteAllBytes(file, PngHeaderBytes(2048, 2048));
        var warnings = new List<string>();

        var processed = Attachments.Process(
            ["big.png"],
            tmp.Path,
            contextWindow: 200_000,
            warnPixels: 4_000_000,
            warningSink: warnings.Add);

        Assert.Contains("note: image is 2048x2048", processed.Text);
        Assert.Single(warnings);
        Assert.Contains(warnings[0], processed.Text);
        var image = Assert.Single(processed.Images);
        Assert.Equal("image/png", image.MimeType);
    }

    // warnPixels = 0 turns the size warning off, per the ADR.
    [Fact]
    public void WarnPixelsZeroDisablesTheAttachmentWarning()
    {
        using var tmp = new TempWorkspace();
        var file = Path.Combine(tmp.Path, "big.png");
        File.WriteAllBytes(file, PngHeaderBytes(3000, 3000));
        var warnings = new List<string>();

        var processed = Attachments.Process(
            ["big.png"],
            tmp.Path,
            contextWindow: 200_000,
            warnPixels: 0,
            warningSink: warnings.Add);

        Assert.DoesNotContain("note: image is", processed.Text);
        Assert.Empty(warnings);
        Assert.Single(processed.Images);
    }

    // The size warning tells the model to crop or downscale before reading. For a model that
    // cannot take images that advice is moot, so it is skipped; the image is still attached.
    [Fact]
    public void TextOnlyModelSkipsTheAttachmentSizeWarning()
    {
        using var tmp = new TempWorkspace();
        var file = Path.Combine(tmp.Path, "big.png");
        File.WriteAllBytes(file, PngHeaderBytes(2048, 2048));
        var warnings = new List<string>();

        var processed = Attachments.Process(
            ["big.png"],
            tmp.Path,
            contextWindow: 200_000,
            warnPixels: 4_000_000,
            warningSink: warnings.Add,
            supportsImages: false);

        Assert.DoesNotContain("note: image is", processed.Text);
        Assert.Empty(warnings);
        Assert.Single(processed.Images);
    }

    private static byte[] PngHeaderBytes(int width, int height) =>
        Convert.FromBase64String(ImageFixtures.Png(width, height).Data);
}
