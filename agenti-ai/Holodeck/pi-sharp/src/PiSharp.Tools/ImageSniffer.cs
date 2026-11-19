// Ported from pi: packages/coding-agent/src/utils/mime.ts @ 71dca871b
//
// Detects the image formats pi accepts by magic bytes: jpeg, png (still only), gif, webp, bmp.
// pi's processImage (resize/re-encode) is not ported: decision 4 keeps images as they are in v1.
using System.Buffers.Binary;
namespace PiSharp.Tools;

/// <summary>Magic-byte detection for the image types the read tool may attach.</summary>
public static class ImageSniffer
{
    private const int SniffBytes = 4100;
    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

    /// <summary>Read the first bytes of a file and detect a supported image type, or null.</summary>
    public static async Task<string?> DetectFromFileAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, useAsync: true);
        var buffer = new byte[SniffBytes];
        var read = await stream.ReadAtLeastAsync(buffer, SniffBytes, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
        return Detect(buffer.AsSpan(0, read));
    }

    /// <summary>Detect a supported image type from a byte prefix, or null when it is not one.</summary>
    public static string? Detect(ReadOnlySpan<byte> buffer)
    {
        if (StartsWith(buffer, [0xff, 0xd8, 0xff]))
        {
            // 0xF7 marks a JPEG-LS stream, which decoders on the model side do not accept.
            return buffer.Length > 3 && buffer[3] == 0xf7 ? null : "image/jpeg";
        }
        if (StartsWith(buffer, PngSignature))
        {
            return IsPng(buffer) && !IsAnimatedPng(buffer) ? "image/png" : null;
        }
        if (StartsWithAscii(buffer, 0, "GIF")) return "image/gif";
        if (StartsWithAscii(buffer, 0, "RIFF") && StartsWithAscii(buffer, 8, "WEBP")) return "image/webp";
        if (StartsWithAscii(buffer, 0, "BM") && IsBmp(buffer)) return "image/bmp";
        return null;
    }

    /// <summary>Read an image's pixel width and height from its header bytes, or false for unknown or truncated headers.</summary>
    public static bool TryGetDimensions(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = height = 0;

        if (StartsWith(data, PngSignature) && data.Length >= PngSignature.Length + 16)
        {
            var pngWidth = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(PngSignature.Length + 8, 4));
            var pngHeight = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(PngSignature.Length + 12, 4));
            if (pngWidth == 0 || pngHeight == 0 || pngWidth > int.MaxValue || pngHeight > int.MaxValue)
            {
                width = height = 0;
                return false;
            }
            width = (int)pngWidth;
            height = (int)pngHeight;
            return true;
        }
        if (StartsWithAscii(data, 0, "GIF87a") || StartsWithAscii(data, 0, "GIF89a"))
        {
            if (data.Length < 10) return false;
            width = (int)BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
            height = (int)BinaryPrimitives.ReadUInt16LittleEndian(data[8..]);
            return true;
        }
        if (StartsWithAscii(data, 0, "BM"))
        {
            if (data.Length < 26) return false;
            width = (int)BinaryPrimitives.ReadInt32LittleEndian(data[18..]);
            var rawHeight = (int)BinaryPrimitives.ReadInt32LittleEndian(data[22..]);
            if (rawHeight == int.MinValue || width <= 0 || rawHeight == 0)
            {
                width = height = 0;
                return false;
            }
            height = Math.Abs(rawHeight);
            return true;
        }
        if (StartsWithAscii(data, 0, "RIFF") && StartsWithAscii(data, 8, "WEBP"))
        {
            if (data.Length < 20) return false;
            var tag = data[12..16];
            if (tag.SequenceEqual("VP8 "u8))
            {
                if (data.Length < 30) return false;
                width = (int)(BinaryPrimitives.ReadUInt16LittleEndian(data[26..]) & 0x3FFF);
                height = (int)(BinaryPrimitives.ReadUInt16LittleEndian(data[28..]) & 0x3FFF);
                return width > 0 && height > 0;
            }
            if (tag.SequenceEqual("VP8L"u8))
            {
                if (data.Length < 25 || data[20] != 0x2F) return false;
                var bits = BinaryPrimitives.ReadUInt32LittleEndian(data[21..]);
                width = (int)((bits & 0x3FFF) + 1);
                height = (int)(((bits >> 14) & 0x3FFF) + 1);
                return true;
            }
            if (tag.SequenceEqual("VP8X"u8))
            {
                if (data.Length < 30) return false;
                width = 1 + data[24] + (data[25] << 8) + (data[26] << 16);
                height = 1 + data[27] + (data[28] << 8) + (data[29] << 16);
                return true;
            }
            return false;
        }
        if (data.Length > 2 && data[0] == 0xFF && data[1] == 0xD8) return TryGetJpegDimensions(data, ref width, ref height);
        return false;
    }

    private static bool TryGetJpegDimensions(ReadOnlySpan<byte> data, ref int width, ref int height)
    {
        var m = 2;
        while (true)
        {
            // A marker is a 0xFF followed by a non-0xFF byte; 0xFF bytes in between are fill.
            while (m + 1 < data.Length && data[m] == 0xFF && data[m + 1] == 0xFF) m++;
            if (m + 2 > data.Length) return false;
            if (data[m] != 0xFF) return false;
            var marker = data[m + 1];

            // Standalone markers without a length: SOI, EOI, RST0-RST9 and TEM.
            if (marker is 0xD8 or 0xD9 or 0x01 or >= 0xD0 and <= 0xD9) return false;

            if (marker is >= 0xC0 and <= 0xC3 or >= 0xC5 and <= 0xC7 or >= 0xC9 and <= 0xCB or >= 0xCD and <= 0xCF)
            {
                if (m + 9 > data.Length) return false;
                height = (int)BinaryPrimitives.ReadUInt16BigEndian(data.Slice(m + 5, 2));
                width = (int)BinaryPrimitives.ReadUInt16BigEndian(data.Slice(m + 7, 2));
                return width > 0 && height > 0;
            }

            if (m + 4 > data.Length) return false;
            var length = (int)BinaryPrimitives.ReadUInt16BigEndian(data.Slice(m + 2, 2));
            if (length < 2 || m + 2 + length > data.Length) return false;
            m += 2 + length;
        }
    }

    private static bool IsPng(ReadOnlySpan<byte> buffer) =>
        buffer.Length >= 16 && ReadUInt32Be(buffer, PngSignature.Length) == 13 && StartsWithAscii(buffer, 12, "IHDR");

    private static bool IsAnimatedPng(ReadOnlySpan<byte> buffer)
    {
        var offset = PngSignature.Length;
        while (offset + 8 <= buffer.Length)
        {
            var chunkLength = ReadUInt32Be(buffer, offset);
            var typeOffset = offset + 4;
            if (StartsWithAscii(buffer, typeOffset, "acTL")) return true;
            if (StartsWithAscii(buffer, typeOffset, "IDAT")) return false;

            var next = (long)offset + 8 + chunkLength + 4;
            if (next <= offset || next > buffer.Length) return false;
            offset = (int)next;
        }
        return false;
    }

    private static bool IsBmp(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < 26) return false;

        var declaredFileSize = ReadUInt32Le(buffer, 2);
        var pixelDataOffset = ReadUInt32Le(buffer, 10);
        var dibHeaderSize = ReadUInt32Le(buffer, 14);
        if (declaredFileSize != 0 && declaredFileSize < 26) return false;
        if (pixelDataOffset < 14 + dibHeaderSize) return false;
        if (declaredFileSize != 0 && pixelDataOffset >= declaredFileSize) return false;

        uint colorPlanes;
        uint bitsPerPixel;
        if (dibHeaderSize == 12)
        {
            colorPlanes = ReadUInt16Le(buffer, 22);
            bitsPerPixel = ReadUInt16Le(buffer, 24);
        }
        else if (dibHeaderSize is >= 40 and <= 124)
        {
            if (buffer.Length < 30) return false;
            colorPlanes = ReadUInt16Le(buffer, 26);
            bitsPerPixel = ReadUInt16Le(buffer, 28);
        }
        else
        {
            return false;
        }

        return colorPlanes == 1 && bitsPerPixel is 1 or 4 or 8 or 16 or 24 or 32;
    }

    private static bool StartsWith(ReadOnlySpan<byte> buffer, ReadOnlySpan<byte> prefix) =>
        buffer.Length >= prefix.Length && buffer[..prefix.Length].SequenceEqual(prefix);

    private static bool StartsWithAscii(ReadOnlySpan<byte> buffer, int offset, string ascii)
    {
        if (offset + ascii.Length > buffer.Length) return false;
        for (var i = 0; i < ascii.Length; i++)
        {
            if (buffer[offset + i] != (byte)ascii[i]) return false;
        }
        return true;
    }

    private static uint ReadUInt32Be(ReadOnlySpan<byte> b, int o) =>
        o + 4 > b.Length ? 0u : ((uint)b[o] << 24) | ((uint)b[o + 1] << 16) | ((uint)b[o + 2] << 8) | b[o + 3];

    private static uint ReadUInt32Le(ReadOnlySpan<byte> b, int o) =>
        o + 4 > b.Length ? 0u : b[o] | ((uint)b[o + 1] << 8) | ((uint)b[o + 2] << 16) | ((uint)b[o + 3] << 24);

    private static uint ReadUInt16Le(ReadOnlySpan<byte> b, int o) =>
        o + 2 > b.Length ? 0u : (uint)(b[o] | (b[o + 1] << 8));
}
