using PiSharp.Tools;

namespace PiSharp.Tests.Tools;

public class ImageSnifferTests
{
    [Fact]
    public void Jpeg()
    {
        Assert.Equal("image/jpeg", ImageSniffer.Detect([0xff, 0xd8, 0xff, 0xe0, 0x00, 0x10]));
    }

    [Fact]
    public void JpegLsRejected()
    {
        Assert.Null(ImageSniffer.Detect([0xff, 0xd8, 0xff, 0xf7]));
    }

    [Fact]
    public void PngStill()
    {
        var png = new byte[]
        {
            0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a,
            0x00, 0x00, 0x00, 0x0d, (byte)'I', (byte)'H', (byte)'D', (byte)'R',
            0, 0, 0, 1, 0, 0, 0, 1, 8, 2, 0, 0, 0,
        };
        Assert.Equal("image/png", ImageSniffer.Detect(png));
    }

    [Fact]
    public void AnimatedPngRejected()
    {
        var png = new List<byte>
        {
            0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a,
            0x00, 0x00, 0x00, 0x0d, (byte)'I', (byte)'H', (byte)'D', (byte)'R',
            0, 0, 0, 1, 0, 0, 0, 1, 8, 2, 0, 0, 0,
            0, 0, 0, 0, // CRC placeholder
        };
        png.AddRange([0x00, 0x00, 0x00, 0x08, (byte)'a', (byte)'c', (byte)'T', (byte)'L', 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0]);
        Assert.Null(ImageSniffer.Detect(png.ToArray()));
    }

    [Fact]
    public void GifWebpBmp()
    {
        Assert.Equal("image/gif", ImageSniffer.Detect("GIF89a"u8.ToArray()));

        var webp = new byte[12];
        "RIFF"u8.CopyTo(webp);
        "WEBP"u8.CopyTo(webp.AsSpan(8));
        Assert.Equal("image/webp", ImageSniffer.Detect(webp));

        var bmp = new byte[32];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        BitConverter.TryWriteBytes(bmp.AsSpan(2), 32u); // file size
        BitConverter.TryWriteBytes(bmp.AsSpan(10), 26u); // pixel offset
        BitConverter.TryWriteBytes(bmp.AsSpan(14), 12u); // BITMAPCOREHEADER
        BitConverter.TryWriteBytes(bmp.AsSpan(22), (ushort)1); // planes
        BitConverter.TryWriteBytes(bmp.AsSpan(24), (ushort)24); // bpp
        Assert.Equal("image/bmp", ImageSniffer.Detect(bmp));
    }

    [Fact]
    public void PlainTextIsNotAnImage()
    {
        Assert.Null(ImageSniffer.Detect("hello world"u8.ToArray()));
    }
}
