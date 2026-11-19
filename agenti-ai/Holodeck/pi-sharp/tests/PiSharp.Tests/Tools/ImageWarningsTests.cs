using PiSharp.Tools;

namespace PiSharp.Tests.Tools;

public sealed class ImageWarningsTests
{
    [Fact]
    public void WarnsWhenPixelsExceedConfiguredLimit()
    {
        var png = PngHeader(2048, 2048);

        var warning = ImageWarnings.For(png, warnPixels: 4_000_000, contextWindow: 200_000);

        Assert.Contains("2048x2048", warning);
        Assert.Contains("~4.1k tokens", warning);
    }

    [Fact]
    public void WarnsWhenTokenEstimateExceedsQuarterOfContext()
    {
        var png = PngHeader(1024, 768);

        var warning = ImageWarnings.For(png, warnPixels: 4_000_000, contextWindow: 2_000);

        Assert.Contains("1024x768", warning);
    }

    [Fact]
    public void DoesNotWarnBelowBothLimits()
    {
        var png = PngHeader(256, 256);

        Assert.Null(ImageWarnings.For(png, warnPixels: 4_000_000, contextWindow: 200_000));
    }

    // warnPixels = 0 turns the size warning off, per adr-pi-sharp-image-size-warning.
    [Fact]
    public void WarnPixelsZeroDisablesTheWarning()
    {
        var png = PngHeader(3000, 3000);

        Assert.Null(ImageWarnings.For(png, warnPixels: 0, contextWindow: 200_000));
        Assert.NotNull(ImageWarnings.For(png, warnPixels: 4_000_000, contextWindow: 200_000));
    }

    private static byte[] PngHeader(int width, int height)
    {
        var data = new byte[24];
        new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }.CopyTo(data, 0);
        data[11] = 13;
        data[12] = (byte)'I';
        data[13] = (byte)'H';
        data[14] = (byte)'D';
        data[15] = (byte)'R';
        data[16] = (byte)(width >> 24);
        data[17] = (byte)(width >> 16);
        data[18] = (byte)(width >> 8);
        data[19] = (byte)width;
        data[20] = (byte)(height >> 24);
        data[21] = (byte)(height >> 16);
        data[22] = (byte)(height >> 8);
        data[23] = (byte)height;
        return data;
    }
}
