// Replaces pi's use of the npm `diff` package (diffLines, createTwoFilesPatch) with a
// hand-written Myers diff, per the zero-dependency rule. Tokenization matches the npm package:
// one token per line including its trailing newline, so "b" and "b\n" are different tokens.
using System.Text;

namespace PiSharp.Tools;

public enum DiffOp
{
    Equal,
    Delete,
    Insert,
}

/// <summary>A run of consecutive lines with the same diff operation. Lines exclude their newline.</summary>
public sealed record DiffPart(DiffOp Op, IReadOnlyList<string> Lines);

/// <summary>Line-based diff and unified patch generation.</summary>
public static class LineDiff
{
    /// <summary>Split into lines that keep their trailing newline, like the npm diff tokenizer.</summary>
    internal static List<string> SplitLinesWithEndings(string content)
    {
        var lines = new List<string>();
        var start = 0;
        while (start < content.Length)
        {
            var newline = content.IndexOf('\n', start);
            if (newline < 0)
            {
                lines.Add(content[start..]);
                break;
            }
            lines.Add(content[start..(newline + 1)]);
            start = newline + 1;
        }
        return lines;
    }

    /// <summary>Diff two texts by line. A change region yields the deleted part first, then the inserted part.</summary>
    public static List<DiffPart> Diff(string oldContent, string newContent)
    {
        var a = SplitLinesWithEndings(oldContent);
        var b = SplitLinesWithEndings(newContent);
        var script = Myers(a, b);

        var parts = new List<DiffPart>();
        var i = 0;
        while (i < script.Count)
        {
            var op = script[i].Op;
            if (op == DiffOp.Equal)
            {
                var run = new List<string>();
                while (i < script.Count && script[i].Op == DiffOp.Equal) run.Add(Strip(script[i++].Line));
                parts.Add(new DiffPart(DiffOp.Equal, run));
                continue;
            }

            // Group a mixed change region into one delete part followed by one insert part.
            var deletes = new List<string>();
            var inserts = new List<string>();
            while (i < script.Count && script[i].Op != DiffOp.Equal)
            {
                if (script[i].Op == DiffOp.Delete) deletes.Add(Strip(script[i].Line));
                else inserts.Add(Strip(script[i].Line));
                i++;
            }
            if (deletes.Count > 0) parts.Add(new DiffPart(DiffOp.Delete, deletes));
            if (inserts.Count > 0) parts.Add(new DiffPart(DiffOp.Insert, inserts));
        }
        return parts;
    }

    private static string Strip(string line) => line.EndsWith('\n') ? line[..^1] : line;

    private readonly record struct Step(DiffOp Op, string Line);

    /// <summary>Myers O(ND) diff with a recorded trace, after trimming the common prefix and suffix.</summary>
    private static List<Step> Myers(List<string> a, List<string> b)
    {
        var script = new List<Step>();

        var prefix = 0;
        while (prefix < a.Count && prefix < b.Count && a[prefix] == b[prefix]) prefix++;
        var suffix = 0;
        while (suffix < a.Count - prefix && suffix < b.Count - prefix && a[a.Count - 1 - suffix] == b[b.Count - 1 - suffix]) suffix++;

        for (var i = 0; i < prefix; i++) script.Add(new Step(DiffOp.Equal, a[i]));

        var aMid = a.GetRange(prefix, a.Count - prefix - suffix);
        var bMid = b.GetRange(prefix, b.Count - prefix - suffix);
        script.AddRange(MyersCore(aMid, bMid));

        for (var i = b.Count - suffix; i < b.Count; i++) script.Add(new Step(DiffOp.Equal, b[i]));
        return script;
    }

    private static List<Step> MyersCore(List<string> a, List<string> b)
    {
        var n = a.Count;
        var m = b.Count;
        if (n == 0) return b.Select(l => new Step(DiffOp.Insert, l)).ToList();
        if (m == 0) return a.Select(l => new Step(DiffOp.Delete, l)).ToList();

        var max = n + m;
        var offset = max;
        var v = new int[2 * max + 1];
        var trace = new List<int[]>();

        for (var d = 0; d <= max; d++)
        {
            trace.Add((int[])v.Clone());
            for (var k = -d; k <= d; k += 2)
            {
                int x;
                if (k == -d || (k != d && v[k - 1 + offset] < v[k + 1 + offset])) x = v[k + 1 + offset];
                else x = v[k - 1 + offset] + 1;

                var y = x - k;
                while (x < n && y < m && a[x] == b[y])
                {
                    x++;
                    y++;
                }
                v[k + offset] = x;

                if (x >= n && y >= m) return Backtrack(a, b, trace, offset, d);
            }
        }
        throw new InvalidOperationException("Myers diff failed to converge");
    }

    private static List<Step> Backtrack(List<string> a, List<string> b, List<int[]> trace, int offset, int d)
    {
        var steps = new List<Step>();
        var x = a.Count;
        var y = b.Count;

        for (var depth = d; depth > 0; depth--)
        {
            var v = trace[depth];
            var k = x - y;
            int prevK;
            if (k == -depth || (k != depth && v[k - 1 + offset] < v[k + 1 + offset])) prevK = k + 1;
            else prevK = k - 1;

            var prevX = v[prevK + offset];
            var prevY = prevX - prevK;

            while (x > prevX && y > prevY)
            {
                steps.Add(new Step(DiffOp.Equal, a[--x]));
                y--;
            }
            if (x > prevX) steps.Add(new Step(DiffOp.Delete, a[--x]));
            else if (y > prevY) steps.Add(new Step(DiffOp.Insert, b[--y]));
        }
        while (x > 0 && y > 0)
        {
            steps.Add(new Step(DiffOp.Equal, a[--x]));
            y--;
        }

        steps.Reverse();
        return steps;
    }

    /// <summary>Standard unified patch with file headers only, matching pi's createTwoFilesPatch options.</summary>
    public static string UnifiedPatch(string path, string oldContent, string newContent, int contextLines = 4)
    {
        var parts = Diff(oldContent, newContent);
        var oldEndsWithNewline = oldContent.Length == 0 || oldContent.EndsWith('\n');
        var newEndsWithNewline = newContent.Length == 0 || newContent.EndsWith('\n');

        // Flatten into per-line entries carrying both file positions.
        var entries = new List<(DiffOp Op, string Line, int OldNo, int NewNo)>();
        var oldNo = 1;
        var newNo = 1;
        foreach (var part in parts)
        {
            foreach (var line in part.Lines)
            {
                switch (part.Op)
                {
                    case DiffOp.Equal:
                        entries.Add((DiffOp.Equal, line, oldNo++, newNo++));
                        break;
                    case DiffOp.Delete:
                        entries.Add((DiffOp.Delete, line, oldNo++, 0));
                        break;
                    default:
                        entries.Add((DiffOp.Insert, line, 0, newNo++));
                        break;
                }
            }
        }

        var totalOldLines = oldNo - 1;
        var totalNewLines = newNo - 1;

        var changed = entries.Select((e, i) => (e, i)).Where(t => t.e.Op != DiffOp.Equal).Select(t => t.i).ToList();
        if (changed.Count == 0) return "";

        var sb = new StringBuilder();
        sb.Append("--- ").Append(path).Append('\n');
        sb.Append("+++ ").Append(path).Append('\n');

        var cursor = 0;
        while (cursor < changed.Count)
        {
            var first = changed[cursor];
            var last = first;
            var next = cursor + 1;
            // Merge change groups whose context windows touch.
            while (next < changed.Count && changed[next] - last <= contextLines * 2)
            {
                last = changed[next];
                next++;
            }

            var start = Math.Max(0, first - contextLines);
            var end = Math.Min(entries.Count - 1, last + contextLines);

            var oldStart = 0;
            var newStart = 0;
            var oldCount = 0;
            var newCount = 0;
            for (var i = start; i <= end; i++)
            {
                var e = entries[i];
                if (e.Op != DiffOp.Insert)
                {
                    if (oldStart == 0) oldStart = e.OldNo;
                    oldCount++;
                }
                if (e.Op != DiffOp.Delete)
                {
                    if (newStart == 0) newStart = e.NewNo;
                    newCount++;
                }
            }
            if (oldCount == 0) oldStart = 0;
            if (newCount == 0) newStart = 0;

            sb.Append("@@ -").Append(oldStart).Append(',').Append(oldCount)
              .Append(" +").Append(newStart).Append(',').Append(newCount).Append(" @@\n");

            for (var i = start; i <= end; i++)
            {
                var e = entries[i];
                sb.Append(e.Op switch { DiffOp.Delete => '-', DiffOp.Insert => '+', _ => ' ' }).Append(e.Line).Append('\n');
                var isLastOld = e.Op != DiffOp.Insert && e.OldNo == totalOldLines;
                var isLastNew = e.Op != DiffOp.Delete && e.NewNo == totalNewLines;
                if ((isLastOld && !oldEndsWithNewline && e.Op != DiffOp.Insert) ||
                    (isLastNew && !newEndsWithNewline && e.Op == DiffOp.Insert))
                {
                    sb.Append("\\ No newline at end of file\n");
                }
            }

            cursor = next;
        }

        return sb.ToString();
    }
}
