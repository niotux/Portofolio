using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using PiSharp.Core.Messages;

namespace PiSharp.Tests.Tools;

internal static class ImageFixtures
{
    // Synthetic equivalent of pi13's 448x312 RGB8 PNG: complete container/zlib, but one
    // byte per row short of the PNG scanline contract. No production capture is embedded.
    public static ImageContent Png(int width = 2, int height = 2, bool missingFilters = false,
        byte depth = 8, byte colour = 2, bool interlaced = false, bool splitIdat = false, int? rawLength = null, int compressedPadding = 0)
    {
        var channels = colour switch { 0 or 3 => 1, 2 => 3, 4 => 2, 6 => 4, _ => 0 };
        var extent = 0;
        if (interlaced)
        {
            int[] xs = [0, 4, 0, 2, 0, 1, 0], ys = [0, 0, 4, 0, 2, 0, 1];
            int[] dx = [8, 8, 4, 4, 2, 2, 1], dy = [8, 8, 8, 4, 4, 2, 2];
            for (var pass = 0; pass < 7; pass++)
            {
                var pixels = Enumerable.Range(0, width).Count(x => x >= xs[pass] && (x - xs[pass]) % dx[pass] == 0);
                if (pixels == 0) continue;
                for (var y = ys[pass]; y < height; y += dy[pass]) extent += 1 + (pixels * channels * depth + 7) / 8;
            }
        }
        else extent = height * ((width * channels * depth + 7) / 8 + (missingFilters ? 0 : 1));
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, true))
            zlib.Write(new byte[rawLength ?? extent]);
        using var png = new MemoryStream();
        png.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = depth;
        header[9] = colour;
        header[12] = interlaced ? (byte)1 : (byte)0;
        Chunk(png, "IHDR", header);
        if (colour == 3) Chunk(png, "PLTE", [0, 0, 0]);
        var encoded = compressed.ToArray();
        if (compressedPadding > 0) Array.Resize(ref encoded, encoded.Length + compressedPadding);
        if (splitIdat)
        {
            Chunk(png, "IDAT", encoded[..3]);
            Chunk(png, "IDAT", encoded[3..]);
        }
        else Chunk(png, "IDAT", encoded);
        Chunk(png, "IEND", []);
        return new ImageContent(Convert.ToBase64String(png.ToArray()), "image/png");
    }

    private static void Chunk(Stream stream, string type, byte[] data)
    {
        Span<byte> n = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(n, data.Length);
        stream.Write(n);
        var body = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        stream.Write(body);
        uint crc = uint.MaxValue;
        foreach (var b in body)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320);
        }
        BinaryPrimitives.WriteUInt32BigEndian(n, ~crc);
        stream.Write(n);
    }
}
