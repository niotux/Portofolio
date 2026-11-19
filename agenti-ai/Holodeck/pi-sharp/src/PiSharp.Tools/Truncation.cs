// Ported from pi: packages/coding-agent/src/core/tools/truncate.ts @ 71dca871b
//
// Truncation uses two independent limits, whichever is hit first wins:
// a line limit (default 2000) and a byte limit (default 50KB).
// Output never contains partial lines, except the tail edge case where a single
// line is larger than the byte budget.
using System.Text;
using System.Text.Json.Nodes;

namespace PiSharp.Tools;

/// <summary>Which limit caused a truncation.</summary>
public enum TruncatedBy
{
    None,
    Lines,
    Bytes,
}

/// <summary>Outcome of a head or tail truncation, including the counts the tools report back to the model.</summary>
public sealed record TruncationResult
{
    /// <summary>The truncated content.</summary>
    public required string Content { get; init; }
    /// <summary>Whether truncation occurred.</summary>
    public required bool Truncated { get; init; }
    /// <summary>Which limit was hit, <see cref="TruncatedBy.None"/> when not truncated.</summary>
    public required TruncatedBy TruncatedBy { get; init; }
    /// <summary>Number of lines in the original content.</summary>
    public required int TotalLines { get; init; }
    /// <summary>Number of UTF-8 bytes in the original content.</summary>
    public required long TotalBytes { get; init; }
    /// <summary>Number of complete lines in the output.</summary>
    public required int OutputLines { get; init; }
    /// <summary>Number of UTF-8 bytes in the output.</summary>
    public required long OutputBytes { get; init; }
    /// <summary>Tail truncation only: the first output line is the end of a longer line.</summary>
    public bool LastLinePartial { get; init; }
    /// <summary>Head truncation only: line 1 alone is bigger than the byte limit, so nothing is shown.</summary>
    public bool FirstLineExceedsLimit { get; init; }
    public required int MaxLines { get; init; }
    public required long MaxBytes { get; init; }

    /// <summary>Machine-readable form for the <c>details</c> field of a tool result.</summary>
    public JsonObject ToJson() => new()
    {
        ["truncated"] = Truncated,
        ["truncatedBy"] = TruncatedBy switch
        {
            TruncatedBy.Lines => "lines",
            TruncatedBy.Bytes => "bytes",
            _ => null,
        },
        ["totalLines"] = TotalLines,
        ["totalBytes"] = TotalBytes,
        ["outputLines"] = OutputLines,
        ["outputBytes"] = OutputBytes,
        ["lastLinePartial"] = LastLinePartial,
        ["firstLineExceedsLimit"] = FirstLineExceedsLimit,
        ["maxLines"] = MaxLines,
        ["maxBytes"] = MaxBytes,
    };
}

/// <summary>Shared truncation helpers for tool output.</summary>
public static class Truncation
{
    public const int DefaultMaxLines = 2000;
    public const int DefaultMaxBytes = 50 * 1024;
    /// <summary>Max characters per grep match line.</summary>
    public const int GrepMaxLineLength = 500;

    /// <summary><c>maxLines</c> value meaning "no line limit" (pi passes Number.MAX_SAFE_INTEGER).</summary>
    public const int NoLineLimit = int.MaxValue;

    internal static int ByteLength(string s) => Encoding.UTF8.GetByteCount(s);

    /// <summary>Split into countable lines: no lines for empty input, and a trailing newline does not add one.</summary>
    internal static List<string> SplitLinesForCounting(string content)
    {
        if (content.Length == 0) return [];
        var lines = new List<string>(content.Split('\n'));
        if (content.EndsWith('\n')) lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    /// <summary>Human-readable byte size, e.g. <c>512B</c>, <c>50.0KB</c>, <c>1.5MB</c>.</summary>
    public static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes}B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1}KB";
        return $"{bytes / (1024.0 * 1024.0):F1}MB";
    }

    /// <summary>
    /// Keep the first lines that fit both limits. Used for file reads.
    /// Never returns a partial line: if line 1 alone exceeds <paramref name="maxBytes"/> the
    /// content is empty and <see cref="TruncationResult.FirstLineExceedsLimit"/> is set.
    /// </summary>
    public static TruncationResult TruncateHead(string content, int? maxLines = null, long? maxBytes = null)
    {
        var lineLimit = maxLines ?? DefaultMaxLines;
        var byteLimit = maxBytes ?? DefaultMaxBytes;

        var totalBytes = (long)ByteLength(content);
        var lines = SplitLinesForCounting(content);
        var totalLines = lines.Count;

        if (totalLines <= lineLimit && totalBytes <= byteLimit)
        {
            return Untruncated(content, totalLines, totalBytes, lineLimit, byteLimit);
        }

        if (ByteLength(lines[0]) > byteLimit)
        {
            return new TruncationResult
            {
                Content = "",
                Truncated = true,
                TruncatedBy = TruncatedBy.Bytes,
                TotalLines = totalLines,
                TotalBytes = totalBytes,
                OutputLines = 0,
                OutputBytes = 0,
                FirstLineExceedsLimit = true,
                MaxLines = lineLimit,
                MaxBytes = byteLimit,
            };
        }

        var kept = new List<string>();
        long keptBytes = 0;
        var by = TruncatedBy.Lines;

        for (var i = 0; i < lines.Count && i < lineLimit; i++)
        {
            // +1 byte for the newline that joins this line to the previous one.
            var lineBytes = ByteLength(lines[i]) + (i > 0 ? 1 : 0);
            if (keptBytes + lineBytes > byteLimit)
            {
                by = TruncatedBy.Bytes;
                break;
            }
            kept.Add(lines[i]);
            keptBytes += lineBytes;
        }

        if (kept.Count >= lineLimit && keptBytes <= byteLimit) by = TruncatedBy.Lines;

        var output = string.Join("\n", kept);
        return new TruncationResult
        {
            Content = output,
            Truncated = true,
            TruncatedBy = by,
            TotalLines = totalLines,
            TotalBytes = totalBytes,
            OutputLines = kept.Count,
            OutputBytes = ByteLength(output),
            MaxLines = lineLimit,
            MaxBytes = byteLimit,
        };
    }

    /// <summary>
    /// Keep the last lines that fit both limits. Used for shell output, where the end matters.
    /// May return a partial first line when the final line of the input is larger than the byte limit.
    /// </summary>
    public static TruncationResult TruncateTail(string content, int? maxLines = null, long? maxBytes = null)
    {
        var lineLimit = maxLines ?? DefaultMaxLines;
        var byteLimit = maxBytes ?? DefaultMaxBytes;

        var totalBytes = (long)ByteLength(content);
        var lines = SplitLinesForCounting(content);
        var totalLines = lines.Count;

        if (totalLines <= lineLimit && totalBytes <= byteLimit)
        {
            return Untruncated(content, totalLines, totalBytes, lineLimit, byteLimit);
        }

        var kept = new List<string>();
        long keptBytes = 0;
        var by = TruncatedBy.Lines;
        var lastLinePartial = false;

        for (var i = lines.Count - 1; i >= 0 && kept.Count < lineLimit; i--)
        {
            var lineBytes = ByteLength(lines[i]) + (kept.Count > 0 ? 1 : 0);
            if (keptBytes + lineBytes > byteLimit)
            {
                by = TruncatedBy.Bytes;
                // Nothing fits yet and this single line is over budget: show its tail.
                if (kept.Count == 0)
                {
                    var partial = TruncateStringToBytesFromEnd(lines[i], byteLimit);
                    kept.Insert(0, partial);
                    keptBytes = ByteLength(partial);
                    lastLinePartial = true;
                }
                break;
            }
            kept.Insert(0, lines[i]);
            keptBytes += lineBytes;
        }

        if (kept.Count >= lineLimit && keptBytes <= byteLimit) by = TruncatedBy.Lines;

        var output = string.Join("\n", kept);
        return new TruncationResult
        {
            Content = output,
            Truncated = true,
            TruncatedBy = by,
            TotalLines = totalLines,
            TotalBytes = totalBytes,
            OutputLines = kept.Count,
            OutputBytes = ByteLength(output),
            LastLinePartial = lastLinePartial,
            MaxLines = lineLimit,
            MaxBytes = byteLimit,
        };
    }

    /// <summary>Keep the last <paramref name="maxBytes"/> bytes of a string, starting at a UTF-8 boundary.</summary>
    internal static string TruncateStringToBytesFromEnd(string s, long maxBytes)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        if (bytes.Length <= maxBytes) return s;

        var start = (int)(bytes.Length - maxBytes);
        // Skip forward past UTF-8 continuation bytes so the result decodes cleanly.
        while (start < bytes.Length && (bytes[start] & 0xc0) == 0x80) start++;
        return Encoding.UTF8.GetString(bytes, start, bytes.Length - start);
    }

    /// <summary>Cut a single line to <paramref name="maxChars"/> characters, used by grep.</summary>
    public static (string Text, bool WasTruncated) TruncateLine(string line, int maxChars = GrepMaxLineLength) =>
        line.Length <= maxChars ? (line, false) : (string.Concat(line.AsSpan(0, maxChars), "... [truncated]"), true);

    private static TruncationResult Untruncated(string content, int totalLines, long totalBytes, int maxLines, long maxBytes) => new()
    {
        Content = content,
        Truncated = false,
        TruncatedBy = TruncatedBy.None,
        TotalLines = totalLines,
        TotalBytes = totalBytes,
        OutputLines = totalLines,
        OutputBytes = totalBytes,
        MaxLines = maxLines,
        MaxBytes = maxBytes,
    };
}
