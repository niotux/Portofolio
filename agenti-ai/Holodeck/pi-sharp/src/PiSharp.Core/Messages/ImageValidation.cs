using System.Buffers.Binary;
using System.IO.Compression;

namespace PiSharp.Core.Messages;

/// <summary>Reject only demonstrably invalid attachments. Not a decoder or an image conversion pipeline.</summary>
public static class ImageValidation
{
    // Validation work bound, NOT an input/pixel limit. Larger legal images pass through unchanged;
    // deterministic provider rejection is handled separately instead of retrying the same request.
    private const long InflateBudget = 64 * 1024 * 1024;
    private const int EncodedWorkBudget = 4 * 1024 * 1024;
    private static ReadOnlySpan<byte> PngSignature => [137, 80, 78, 71, 13, 10, 26, 10];

    public static bool IsImageFileName(string path) => Path.GetExtension(path).ToLowerInvariant()
        is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp";

    private sealed record ValidationResult(string? Problem);
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ImageContent, ValidationResult> Cache = new();

    public static string? Problem(ImageContent image) => Cache.GetValue(image,
        static i => new ValidationResult(Problem(i.Data, i.MimeType))).Problem;

    private static string? Problem(string base64, string mime)
    {
        byte[] bytes;
        try { bytes = Convert.FromBase64String(base64 ?? ""); }
        catch (FormatException) { return "invalid base64 (decoded byte count unavailable)"; }
        return Problem(bytes, mime);
    }

    public static string? Problem(byte[] bytes, string mime)
    {
        if (bytes.Length == 0) return "empty image (0 bytes)";
        if (string.IsNullOrWhiteSpace(mime)) return $"missing image MIME ({bytes.Length} bytes)";
        var data = bytes.AsSpan();
        var png = data.StartsWith(PngSignature);
        var matchesMime = mime.ToLowerInvariant() switch
        {
            "image/png" => png,
            "image/jpeg" or "image/jpg" => data.StartsWith(new byte[] { 0xff, 0xd8, 0xff }),
            "image/gif" => data.StartsWith("GIF87a"u8) || data.StartsWith("GIF89a"u8),
            "image/webp" => data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data.Slice(8, 4).SequenceEqual("WEBP"u8),
            "image/bmp" => data.StartsWith("BM"u8),
            _ => true, // Unsupported types pass through, not proof of invalid bytes.
        };
        if (!matchesMime) return $"image signature does not match declared MIME ({bytes.Length} bytes)";
        if (!png) return null; // Other nonempty formats are not claimed to be fully decoded here.
        var reason = PngProblem(bytes);
        return reason is null ? null : $"{reason} ({bytes.Length} bytes)";
    }

    public static void RequireValid(byte[] bytes, string mime, string reference)
    {
        if (Problem(bytes, mime) is { } problem)
            throw new InvalidDataException(Diagnostic(reference, problem));
    }

    public static string Diagnostic(string reference, string problem) =>
        $"[invalid image omitted: {SafeReference(reference)}: {problem}. Original source/session preserved; recapture or supply a valid image.]";

    public static IReadOnlyList<ContentPart> Project(IReadOnlyList<ContentPart> content, string reference, out bool rejected)
    {
        rejected = false;
        List<ContentPart>? output = null;
        for (var i = 0; i < content.Count; i++)
        {
            var part = content[i];
            if (part is ImageContent image && Problem(image) is { } problem)
            {
                output ??= content.Take(i).ToList();
                output.Add(new TextContent(Diagnostic($"{reference} part {i}", problem)));
                rejected = true;
            }
            else output?.Add(part);
        }
        return output ?? content;
    }

    private static string SafeReference(string reference) => new(reference.Take(240)
        .Select(c => char.IsControl(c) ? ' ' : c).ToArray());

    private static string? PngProblem(byte[] bytes)
    {
        var offset = 8;
        var headerSeen = false;
        var endSeen = false;
        byte depth = 0, colour = 0, interlace = 0;
        uint width = 0, height = 0;
        long expected = 0;
        using var compressed = new MemoryStream();
        while (offset <= bytes.Length - 12)
        {
            var size = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset));
            if (size > bytes.Length - offset - 12) return "truncated PNG chunk";
            var length = (int)size;
            var kind = bytes.AsSpan(offset + 4, 4);
            var data = bytes.AsSpan(offset + 8, length);
            if (!headerSeen && !kind.SequenceEqual("IHDR"u8)) return "PNG lacks first IHDR chunk";
            if (kind.SequenceEqual("IHDR"u8) && (headerSeen || length != 13)) return "invalid PNG IHDR chunk";
            if (Crc(bytes.AsSpan(offset + 4, length + 4)) != BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 8 + length)))
                return "PNG chunk checksum mismatch";
            if (kind.SequenceEqual("IHDR"u8))
            {
                if (headerSeen || length != 13) return "invalid PNG IHDR chunk";
                headerSeen = true;
                width = BinaryPrimitives.ReadUInt32BigEndian(data);
                height = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
                depth = data[8]; colour = data[9]; interlace = data[12];
                if (width == 0 || height == 0 || width > int.MaxValue || height > int.MaxValue)
                    return "invalid PNG dimensions";
                if (data[10] != 0 || data[11] != 0 || interlace > 1) return "invalid PNG encoding method";
                var channels = colour switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 0 };
                var legalDepth = colour switch
                {
                    0 => depth is 1 or 2 or 4 or 8 or 16,
                    3 => depth is 1 or 2 or 4 or 8,
                    2 or 4 or 6 => depth is 8 or 16,
                    _ => false,
                };
                if (!legalDepth) return "invalid PNG colour type/bit depth";
                try { expected = ScanlineExtent(width, height, channels, depth, interlace); }
                catch (OverflowException) { return null; } // Unsupported extent, not proof of invalid bytes.
                // Decide before any IDAT copy or large CRC walk. These are validation-work limits,
                // not attachment limits: unsupported large input remains unchanged for the provider.
                if (expected > InflateBudget || bytes.Length > EncodedWorkBudget) return null;
            }
            else if (kind.SequenceEqual("IDAT"u8)) compressed.Write(data);
            else if (kind.SequenceEqual("IEND"u8))
            {
                if (length != 0) return "invalid PNG IEND chunk";
                endSeen = true;
                break;
            }
            offset += length + 12;
        }
        if (!headerSeen || !endSeen || compressed.Length == 0) return "truncated PNG (missing IHDR, IDAT or IEND)";
        compressed.Position = 0;
        try
        {
            using var inflater = new ZLibStream(compressed, CompressionMode.Decompress, leaveOpen: true);
            Span<byte> buffer = stackalloc byte[8192];
            long count = 0;
            uint a = 1, b = 0;
            int read;
            while ((read = inflater.Read(buffer[..(int)Math.Min(buffer.Length, expected + 1 - count)])) > 0)
            {
                count += read;
                foreach (var value in buffer[..read]) { a = (a + value) % 65521; b = (b + a) % 65521; }
                if (count > expected) break;
            }
            if (count != expected) return $"PNG scanline extent is {count} bytes; expected {expected}";
            // ZLibStream can accept a stream missing its trailer: verify the RFC1950 checksum too.
            var encoded = compressed.GetBuffer().AsSpan(0, (int)compressed.Length);
            if (encoded.Length < 6 || BinaryPrimitives.ReadUInt32BigEndian(encoded[^4..]) != (b << 16 | a))
                return "truncated or damaged PNG zlib checksum";
        }
        catch (InvalidDataException) { return "damaged PNG compressed scanlines"; }
        return null;
    }

    private static long ScanlineExtent(uint width, uint height, int channels, byte depth, byte interlace)
    {
        long Extent(long w, long h) => w == 0 || h == 0 ? 0 : checked(h * (1 + (w * channels * depth + 7) / 8));
        if (interlace == 0) return Extent(width, height);
        // Adam7 origins/strides. Packed low-bit-depth rows round up per pass, skipping empty passes.
        int[] x = [0, 4, 0, 2, 0, 1, 0], y = [0, 0, 4, 0, 2, 0, 1];
        int[] dx = [8, 8, 4, 4, 2, 2, 1], dy = [8, 8, 8, 4, 4, 2, 2];
        long total = 0;
        for (var p = 0; p < 7; p++)
            total = checked(total + Extent(Math.Max(0, ((long)width - x[p] + dx[p] - 1) / dx[p]),
                Math.Max(0, ((long)height - y[p] + dy[p] - 1) / dy[p])));
        return total;
    }

    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320);
        }
        return ~crc;
    }
}
