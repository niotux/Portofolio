using System.Buffers.Binary;
using System.Text;
using PiSharp.Tools;

namespace PiSharp.Tests.Tools;

public class ImageDimensionsTests
{
    [Fact]
    public void Png640x480()
    {
        var data = new byte[33];
        new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }.CopyTo(data);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), 13u);
        "IHDR"u8.CopyTo(data.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(16), 640u);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(20), 480u);
        Assert.True(ImageSniffer.TryGetDimensions(data, out var w, out var h));
        Assert.Equal((640, 480), (w, h));
    }

    [Fact]
    public void PngTruncatedTo20Bytes()
    {
        var data = new byte[]
        {
            0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a,
            0x00, 0x00, 0x00, 0x0d, (byte)'I', (byte)'H', (byte)'D', (byte)'R',
            0, 0, 0, 0x0,
        }; // 20 bytes: IHDR present but width/height cut off
        Assert.False(ImageSniffer.TryGetDimensions(data, out var w, out var h));
        Assert.Equal((0, 0), (w, h));
    }

    [Fact]
    public void Gif320x200()
    {
        var data = new byte[10];
        "GIF89a"u8.CopyTo(data);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6), 320);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), 200);
        Assert.True(ImageSniffer.TryGetDimensions(data, out var w, out var h));
        Assert.Equal((320, 200), (w, h));
    }

    [Fact]
    public void Bmp100xNegative50Becomes50()
    {
        var data = new byte[26];
        data[0] = (byte)'B';
        data[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(18), 100);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(22), -50);
        Assert.True(ImageSniffer.TryGetDimensions(data, out var w, out var h));
        Assert.Equal((100, 50), (w, h));
    }

    [Fact]
    public void BmpHeightIntMinValueReturnsFalse()
    {
        var data = new byte[26];
        data[0] = (byte)'B';
        data[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(18), 100);
        data[22] = 0x00; // int.MinValue height: 00 00 00 80
        data[23] = 0x00;
        data[24] = 0x00;
        data[25] = 0x80;
        Assert.False(ImageSniffer.TryGetDimensions(data, out var w, out var h));
        Assert.Equal((0, 0), (w, h));
    }

    [Fact]
    public void BmpZeroWidthReturnsFalse()
    {
        var data = new byte[26];
        data[0] = (byte)'B';
        data[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(18), 0);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(22), 50);
        Assert.False(ImageSniffer.TryGetDimensions(data, out var w, out var h));
        Assert.Equal((0, 0), (w, h));
    }

    [Fact]
    public void PngWidthGreaterThanIntMaxReturnsFalse()
    {
        var data = new byte[33];
        new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }.CopyTo(data);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), 13u);
        "IHDR"u8.CopyTo(data.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(16), 0x80000000u);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(20), 480u);
        Assert.False(ImageSniffer.TryGetDimensions(data, out var w, out var h));
        Assert.Equal((0, 0), (w, h));
    }

    [Fact]
    public void WebpVp8Lossy1024x768()
    {
        var data = new byte[30];
        "RIFF"u8.CopyTo(data);
        "WEBP"u8.CopyTo(data.AsSpan(8));
        "VP8 "u8.CopyTo(data.AsSpan(12));
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16), 10);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(26), (ushort)1024);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(28), (ushort)768);
        Assert.True(ImageSniffer.TryGetDimensions(data, out var w, out var h));
        Assert.Equal((1024, 768), (w, h));
    }

    [Fact]
    public void WebpVp8L17x9()
    {
        var data = new byte[25];
        "RIFF"u8.CopyTo(data);
        "WEBP"u8.CopyTo(data.AsSpan(8));
        "VP8L"u8.CopyTo(data.AsSpan(12));
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16), 10);
        data[20] = 0x2F;
        var bits = (uint)((17 - 1) | ((9 - 1) << 14));
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(21), bits);
        Assert.True(ImageSniffer.TryGetDimensions(data, out var w, out var h));
        Assert.Equal((17, 9), (w, h));
    }

    [Fact]
    public void WebpVp8X4000x3000()
    {
        var data = new byte[30];
        "RIFF"u8.CopyTo(data);
        "WEBP"u8.CopyTo(data.AsSpan(8));
        "VP8X"u8.CopyTo(data.AsSpan(12));
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16), 10);
        var width24 = 4000 - 1;
        var height24 = 3000 - 1;
        data[24] = (byte)(width24 & 0xFF);
        data[25] = (byte)((width24 >> 8) & 0xFF);
        data[26] = (byte)((width24 >> 16) & 0xFF);
        data[27] = (byte)(height24 & 0xFF);
        data[28] = (byte)((height24 >> 8) & 0xFF);
        data[29] = (byte)((height24 >> 16) & 0xFF);
        Assert.True(ImageSniffer.TryGetDimensions(data, out var w, out var h));
        Assert.Equal((4000, 3000), (w, h));
    }

    [Fact]
    public void JpegApp0ThenSof0_1920x1080()
    {
        var app0Payload = new byte[16 - 2];
        Encoding.Unicode.GetBytes("JFIF").CopyTo(app0Payload, 2);
        var data = new byte[2 + app0Payload.Length + 2 + 12];
        data[0] = 0xFF;
        data[1] = 0xD8;
        data[2] = 0xFF;
        data[3] = 0xE0;
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(4), (ushort)(app0Payload.Length + 2));
        app0Payload.CopyTo(data.AsSpan(6));
        var sof = 6 + app0Payload.Length;
        data[sof] = 0xFF;
        data[sof + 1] = 0xC0;
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(sof + 2), (ushort)11); // 9 + component count
        data[sof + 4] = 8;
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(sof + 5), (ushort)1080);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(sof + 7), (ushort)1920);
        data[sof + 9] = 1; // component count (length 9 = 7 + count)
        Assert.True(ImageSniffer.TryGetDimensions(data, out var w, out var h));
        Assert.Equal((1920, 1080), (w, h));
    }

    [Fact]
    public void JpegSof2Progressive800x600()
    {
        var data = new byte[14];
        data[0] = 0xFF;
        data[1] = 0xD8;
        data[2] = 0xFF;
        data[3] = 0xC2;
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(4), (ushort)19);
        data[6] = 8;
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(7), (ushort)600);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(9), (ushort)800);
        data[11] = 2; // component count
        data[12] = 1;
        data[13] = 1;
        Assert.True(ImageSniffer.TryGetDimensions(data, out var w, out var h));
        Assert.Equal((800, 600), (w, h));
    }

    [Fact]
    public void EmptySpan()
    {
        Assert.False(ImageSniffer.TryGetDimensions(ReadOnlySpan<byte>.Empty, out var w, out var h));
        Assert.Equal((0, 0), (w, h));
    }

    [Fact]
    public void UnknownBytes()
    {
        Assert.False(ImageSniffer.TryGetDimensions("just some random text bytes"u8.ToArray(), out var w, out var h));
        Assert.Equal((0, 0), (w, h));
    }

    [Fact]
    public void JpegWithoutSof()
    {
        // SOI, an APP1 segment, then the stream ends: never an SOF.
        var data = new byte[2 + 2 + 2 + 4];
        data[0] = 0xFF;
        data[1] = 0xD8;
        data[2] = 0xFF;
        data[3] = 0xE1;
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(4), 6);
        Assert.False(ImageSniffer.TryGetDimensions(data, out var w, out var h));
        Assert.Equal((0, 0), (w, h));
    }
}
