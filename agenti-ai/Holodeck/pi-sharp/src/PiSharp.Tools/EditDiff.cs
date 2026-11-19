// Ported from pi: packages/coding-agent/src/core/tools/edit-diff.ts @ 71dca871b
using System.Globalization;
using System.Text;

namespace PiSharp.Tools;

/// <summary>One targeted replacement, matched against the original file (not incrementally).</summary>
public readonly record struct TextEdit(string OldText, string NewText);

/// <summary>Result of applying edits to LF-normalized content.</summary>
public readonly record struct AppliedEdits(string BaseContent, string NewContent);

/// <summary>Display-oriented diff with line numbers, plus the first changed line in the new file.</summary>
public readonly record struct DisplayDiff(string Diff, int? FirstChangedLine);

/// <summary>
/// Fuzzy matching, multi-edit application, and display diffs for the edit tool.
/// Unified patches go through <see cref="LineDiff.UnifiedPatch"/>.
/// </summary>
public static class EditDiff
{
    public static string DetectLineEnding(string content)
    {
        var crlfIdx = content.IndexOf("\r\n", StringComparison.Ordinal);
        var lfIdx = content.IndexOf('\n');
        if (lfIdx < 0) return "\n";
        if (crlfIdx < 0) return "\n";
        return crlfIdx < lfIdx ? "\r\n" : "\n";
    }

    public static string NormalizeToLf(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    public static string RestoreLineEndings(string text, string ending) =>
        ending == "\r\n" ? text.Replace("\n", "\r\n", StringComparison.Ordinal) : text;

    /// <summary>Split a leading UTF-8 BOM from decoded text. Port of <c>utils/text.ts</c> <c>splitBom</c>.</summary>
    public static (string Bom, string Text) SplitBom(string content) =>
        content.StartsWith('\uFEFF') ? ("\uFEFF", content[1..]) : ("", content);

    /// <summary>
    /// Progressive normalization used when exact matching fails: NFKC, trailing whitespace,
    /// smart quotes, dashes, and unicode spaces.
    /// </summary>
    public static string NormalizeForFuzzyMatch(string text)
    {
        var nfkc = text.Normalize(NormalizationForm.FormKC);
        var lines = nfkc.Split('\n');
        for (var i = 0; i < lines.Length; i++) lines[i] = lines[i].TrimEnd();
        var joined = string.Join('\n', lines);
        var sb = new StringBuilder(joined.Length);
        foreach (var ch in joined)
        {
            sb.Append(ch switch
            {
                '\u2018' or '\u2019' or '\u201A' or '\u201B' => '\'',
                '\u201C' or '\u201D' or '\u201E' or '\u201F' => '"',
                '\u2010' or '\u2011' or '\u2012' or '\u2013' or '\u2014' or '\u2015' or '\u2212' => '-',
                '\u00A0' or '\u2002' or '\u2003' or '\u2004' or '\u2005' or '\u2006'
                    or '\u2007' or '\u2008' or '\u2009' or '\u200A' or '\u202F' or '\u205F' or '\u3000' => ' ',
                _ => ch,
            });
        }
        return sb.ToString();
    }

    public readonly record struct FuzzyMatchResult(
        bool Found,
        int Index,
        int MatchLength,
        bool UsedFuzzyMatch,
        string ContentForReplacement);

    /// <summary>Find <paramref name="oldText"/> in <paramref name="content"/>, exact first then fuzzy.</summary>
    public static FuzzyMatchResult FuzzyFindText(string content, string oldText)
    {
        var exactIndex = content.IndexOf(oldText, StringComparison.Ordinal);
        if (exactIndex >= 0)
        {
            return new FuzzyMatchResult(true, exactIndex, oldText.Length, false, content);
        }

        var fuzzyContent = NormalizeForFuzzyMatch(content);
        var fuzzyOldText = NormalizeForFuzzyMatch(oldText);
        var fuzzyIndex = fuzzyContent.IndexOf(fuzzyOldText, StringComparison.Ordinal);
        if (fuzzyIndex < 0)
        {
            return new FuzzyMatchResult(false, -1, 0, false, content);
        }

        return new FuzzyMatchResult(true, fuzzyIndex, fuzzyOldText.Length, true, fuzzyContent);
    }

    /// <summary>
    /// Apply one or more exact-text replacements to LF-normalized content.
    /// All edits are matched against the same original; replacements apply in reverse order.
    /// Fuzzy matches run in normalized space and overlay onto original unchanged line blocks.
    /// With <paramref name="fuzzy"/> false (the default) only exact matches count, and uniqueness
    /// is counted exactly too.
    /// </summary>
    public static AppliedEdits ApplyEditsToNormalizedContent(
        string normalizedContent, IReadOnlyList<TextEdit> edits, string path, bool fuzzy = false)
    {
        var normalizedEdits = new TextEdit[edits.Count];
        for (var i = 0; i < edits.Count; i++)
        {
            normalizedEdits[i] = new TextEdit(NormalizeToLf(edits[i].OldText), NormalizeToLf(edits[i].NewText));
            if (normalizedEdits[i].OldText.Length == 0) throw EmptyOldTextError(path, i, normalizedEdits.Length);
        }

        var initialMatches = new FuzzyMatchResult[normalizedEdits.Length];
        var usedFuzzyMatch = false;
        for (var i = 0; i < normalizedEdits.Length; i++)
        {
            initialMatches[i] = FindText(normalizedContent, normalizedEdits[i].OldText, fuzzy);
            if (initialMatches[i].UsedFuzzyMatch) usedFuzzyMatch = true;
        }

        var replacementBase = usedFuzzyMatch ? NormalizeForFuzzyMatch(normalizedContent) : normalizedContent;

        var matched = new List<MatchedEdit>(normalizedEdits.Length);
        for (var i = 0; i < normalizedEdits.Length; i++)
        {
            var match = FindText(replacementBase, normalizedEdits[i].OldText, fuzzy);
            if (!match.Found) throw NotFoundError(path, i, normalizedEdits.Length);

            var occurrences = fuzzy
                ? CountOccurrences(replacementBase, normalizedEdits[i].OldText)
                : CountExact(replacementBase, normalizedEdits[i].OldText);
            if (occurrences > 1) throw DuplicateError(path, i, normalizedEdits.Length, occurrences);

            matched.Add(new MatchedEdit(i, match.Index, match.MatchLength, normalizedEdits[i].NewText));
        }

        matched.Sort(static (a, b) => a.MatchIndex.CompareTo(b.MatchIndex));
        for (var i = 1; i < matched.Count; i++)
        {
            var previous = matched[i - 1];
            var current = matched[i];
            if (previous.MatchIndex + previous.MatchLength > current.MatchIndex)
            {
                throw new InvalidOperationException(
                    $"edits[{previous.EditIndex}] and edits[{current.EditIndex}] overlap in {path}. Merge them into one edit or target disjoint regions.");
            }
        }

        IReadOnlyList<TextReplacement> replacements = matched.ConvertAll(static m => (TextReplacement)m);
        var newContent = usedFuzzyMatch
            ? ApplyReplacementsPreservingUnchangedLines(normalizedContent, replacementBase, replacements)
            : ApplyReplacements(replacementBase, replacements);

        if (normalizedContent == newContent) throw NoChangeError(path, normalizedEdits.Length);
        return new AppliedEdits(normalizedContent, newContent);
    }

    /// <summary>
    /// Apply replacements matched against <paramref name="baseContent"/> to <paramref name="originalContent"/>
    /// while copying untouched line blocks from the original.
    /// </summary>
    public static string ApplyReplacementsPreservingUnchangedLines(
        string originalContent, string baseContent, IReadOnlyList<TextReplacement> replacements)
    {
        var originalLines = SplitLinesWithEndings(originalContent);
        var baseLines = GetLineSpans(baseContent);
        if (originalLines.Count != baseLines.Count)
        {
            throw new InvalidOperationException("Cannot preserve unchanged lines because the base content has a different line count.");
        }

        var groups = new List<(int StartLine, int EndLine, List<TextReplacement> Replacements)>();
        var sorted = replacements.OrderBy(r => r.MatchIndex).ToList();
        foreach (var replacement in sorted)
        {
            var range = GetReplacementLineRange(baseLines, replacement);
            if (groups.Count > 0 && range.StartLine < groups[^1].EndLine)
            {
                var current = groups[^1];
                current.EndLine = Math.Max(current.EndLine, range.EndLine);
                current.Replacements.Add(replacement);
                groups[^1] = current;
                continue;
            }
            groups.Add((range.StartLine, range.EndLine, [replacement]));
        }

        var originalLineIndex = 0;
        var result = new StringBuilder();
        foreach (var group in groups)
        {
            for (var i = originalLineIndex; i < group.StartLine; i++) result.Append(originalLines[i]);

            var groupStartOffset = baseLines[group.StartLine].Start;
            var groupEndOffset = baseLines[group.EndLine - 1].End;
            result.Append(ApplyReplacements(
                baseContent[groupStartOffset..groupEndOffset],
                group.Replacements,
                groupStartOffset));
            originalLineIndex = group.EndLine;
        }
        for (var i = originalLineIndex; i < originalLines.Count; i++) result.Append(originalLines[i]);
        return result.ToString();
    }

    public static DisplayDiff GenerateDiffString(string oldContent, string newContent, int contextLines = 4)
    {
        var parts = LineDiff.Diff(oldContent, newContent);
        var output = new List<string>();

        var oldLines = oldContent.Split('\n');
        var newLines = newContent.Split('\n');
        var maxLineNum = Math.Max(oldLines.Length, newLines.Length);
        var lineNumWidth = maxLineNum.ToString(CultureInfo.InvariantCulture).Length;

        var oldLineNum = 1;
        var newLineNum = 1;
        var lastWasChange = false;
        int? firstChangedLine = null;

        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            var raw = part.Lines;

            if (part.Op is DiffOp.Insert or DiffOp.Delete)
            {
                firstChangedLine ??= newLineNum;
                foreach (var line in raw)
                {
                    if (part.Op == DiffOp.Insert)
                    {
                        output.Add($"+{newLineNum.ToString(CultureInfo.InvariantCulture).PadLeft(lineNumWidth)} {line}");
                        newLineNum++;
                    }
                    else
                    {
                        output.Add($"-{oldLineNum.ToString(CultureInfo.InvariantCulture).PadLeft(lineNumWidth)} {line}");
                        oldLineNum++;
                    }
                }
                lastWasChange = true;
            }
            else
            {
                var nextPartIsChange = i < parts.Count - 1 && parts[i + 1].Op is DiffOp.Insert or DiffOp.Delete;
                var hasLeadingChange = lastWasChange;
                var hasTrailingChange = nextPartIsChange;

                if (hasLeadingChange && hasTrailingChange)
                {
                    if (raw.Count <= contextLines * 2)
                    {
                        foreach (var line in raw)
                        {
                            output.Add($" {oldLineNum.ToString(CultureInfo.InvariantCulture).PadLeft(lineNumWidth)} {line}");
                            oldLineNum++;
                            newLineNum++;
                        }
                    }
                    else
                    {
                        var skippedLines = raw.Count - contextLines * 2;
                        for (var j = 0; j < contextLines; j++)
                        {
                            output.Add($" {oldLineNum.ToString(CultureInfo.InvariantCulture).PadLeft(lineNumWidth)} {raw[j]}");
                            oldLineNum++;
                            newLineNum++;
                        }
                        output.Add($" {new string(' ', lineNumWidth)} ...");
                        oldLineNum += skippedLines;
                        newLineNum += skippedLines;
                        for (var j = raw.Count - contextLines; j < raw.Count; j++)
                        {
                            output.Add($" {oldLineNum.ToString(CultureInfo.InvariantCulture).PadLeft(lineNumWidth)} {raw[j]}");
                            oldLineNum++;
                            newLineNum++;
                        }
                    }
                }
                else if (hasLeadingChange)
                {
                    var shown = Math.Min(contextLines, raw.Count);
                    var skippedLines = raw.Count - shown;
                    for (var j = 0; j < shown; j++)
                    {
                        output.Add($" {oldLineNum.ToString(CultureInfo.InvariantCulture).PadLeft(lineNumWidth)} {raw[j]}");
                        oldLineNum++;
                        newLineNum++;
                    }
                    if (skippedLines > 0)
                    {
                        output.Add($" {new string(' ', lineNumWidth)} ...");
                        oldLineNum += skippedLines;
                        newLineNum += skippedLines;
                    }
                }
                else if (hasTrailingChange)
                {
                    var skippedLines = Math.Max(0, raw.Count - contextLines);
                    if (skippedLines > 0)
                    {
                        output.Add($" {new string(' ', lineNumWidth)} ...");
                        oldLineNum += skippedLines;
                        newLineNum += skippedLines;
                    }
                    for (var j = skippedLines; j < raw.Count; j++)
                    {
                        output.Add($" {oldLineNum.ToString(CultureInfo.InvariantCulture).PadLeft(lineNumWidth)} {raw[j]}");
                        oldLineNum++;
                        newLineNum++;
                    }
                }
                else
                {
                    oldLineNum += raw.Count;
                    newLineNum += raw.Count;
                }

                lastWasChange = false;
            }
        }

        return new DisplayDiff(string.Join('\n', output), firstChangedLine);
    }

    public interface TextReplacement
    {
        int MatchIndex { get; }
        int MatchLength { get; }
        string NewText { get; }
    }

    private readonly record struct MatchedEdit(int EditIndex, int MatchIndex, int MatchLength, string NewText) : TextReplacement;

    private readonly record struct LineSpan(int Start, int End);

    private static List<string> SplitLinesWithEndings(string content)
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

    private static List<LineSpan> GetLineSpans(string content)
    {
        var lines = SplitLinesWithEndings(content);
        var spans = new List<LineSpan>(lines.Count);
        var offset = 0;
        foreach (var line in lines)
        {
            spans.Add(new LineSpan(offset, offset + line.Length));
            offset += line.Length;
        }
        return spans;
    }

    private static (int StartLine, int EndLine) GetReplacementLineRange(List<LineSpan> lines, TextReplacement replacement)
    {
        var replacementStart = replacement.MatchIndex;
        var replacementEnd = replacement.MatchIndex + replacement.MatchLength;

        var startLine = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            if (replacementStart >= lines[i].Start && replacementStart < lines[i].End)
            {
                startLine = i;
                break;
            }
        }
        if (startLine < 0) throw new InvalidOperationException("Replacement range is outside the base content.");

        var endLine = startLine;
        while (endLine < lines.Count && lines[endLine].End < replacementEnd) endLine++;
        if (endLine >= lines.Count) throw new InvalidOperationException("Replacement range is outside the base content.");
        return (startLine, endLine + 1);
    }

    private static string ApplyReplacements(string content, IReadOnlyList<TextReplacement> replacements, int offset = 0)
    {
        var result = content;
        for (var i = replacements.Count - 1; i >= 0; i--)
        {
            var replacement = replacements[i];
            var matchIndex = replacement.MatchIndex - offset;
            result = string.Concat(result.AsSpan(0, matchIndex), replacement.NewText, result.AsSpan(matchIndex + replacement.MatchLength));
        }
        return result;
    }

    private static FuzzyMatchResult FindText(string content, string oldText, bool fuzzy)
    {
        if (fuzzy) return FuzzyFindText(content, oldText);
        var index = content.IndexOf(oldText, StringComparison.Ordinal);
        return index >= 0
            ? new FuzzyMatchResult(true, index, oldText.Length, false, content)
            : new FuzzyMatchResult(false, -1, 0, false, content);
    }

    private static int CountOccurrences(string content, string oldText) =>
        CountExact(NormalizeForFuzzyMatch(content), NormalizeForFuzzyMatch(oldText));

    private static int CountExact(string content, string oldText)
    {
        if (oldText.Length == 0) return 0;
        var count = 0;
        var start = 0;
        while (true)
        {
            var idx = content.IndexOf(oldText, start, StringComparison.Ordinal);
            if (idx < 0) break;
            count++;
            start = idx + Math.Max(oldText.Length, 1);
        }
        return count;
    }

    private static InvalidOperationException NotFoundError(string path, int editIndex, int totalEdits) =>
        totalEdits == 1
            ? new($"Could not find the exact text in {path}. The old text must match exactly including all whitespace and newlines.")
            : new($"Could not find edits[{editIndex}] in {path}. The oldText must match exactly including all whitespace and newlines.");

    private static InvalidOperationException DuplicateError(string path, int editIndex, int totalEdits, int occurrences) =>
        totalEdits == 1
            ? new($"Found {occurrences} occurrences of the text in {path}. The text must be unique. Please provide more context to make it unique.")
            : new($"Found {occurrences} occurrences of edits[{editIndex}] in {path}. Each oldText must be unique. Please provide more context to make it unique.");

    private static InvalidOperationException EmptyOldTextError(string path, int editIndex, int totalEdits) =>
        totalEdits == 1
            ? new($"oldText must not be empty in {path}.")
            : new($"edits[{editIndex}].oldText must not be empty in {path}.");

    private static InvalidOperationException NoChangeError(string path, int totalEdits) =>
        totalEdits == 1
            ? new($"No changes made to {path}. The replacement produced identical content. This might indicate an issue with special characters or the text not existing as expected.")
            : new($"No changes made to {path}. The replacements produced identical content.");
}
