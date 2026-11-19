// apply_patch: the Codex patch format (codex-rs/apply-patch). Not in pi. Models trained on Codex emit this
// envelope unprompted; one call can add, update, move and delete several files.
//
//   *** Begin Patch
//   *** Add File: path          then '+' lines
//   *** Update File: path       optional '*** Move to: path', then '@@ [context line]' hunks of ' '/'-'/'+' lines
//   *** Delete File: path
//   *** End Patch
//
// Atomic: every hunk of every file is validated before anything is written, and a failure part-way through
// the writes restores what was already written. Hunks match in Codex's four passes (seek_sequence, 2026-10-25,
// plan-pi-sharp-codex item 7): exactly, then ignoring trailing whitespace, then ignoring whitespace on both sides, then
// also reading typographic dashes, quotes and spaces as their plain ASCII forms; nothing looser.
using System.Text;
using System.Text.Json.Nodes;
using PiSharp.Core.Agent;

namespace PiSharp.Tools;

/// <summary>Multi-file patch in the Codex <c>*** Begin Patch</c> format, applied all-or-nothing.</summary>
public sealed class PatchTool : BuiltinTool<PatchArgs>
{
    private static readonly JsonObject ArgsSchema = Schema.Parse("""
        {
          "type": "object",
          "properties": {
            "input": {
              "type": "string",
              "description": "The whole patch, from '*** Begin Patch' to '*** End Patch'."
            }
          },
          "required": ["input"]
        }
        """);

    private const string Describe =
        "Apply a patch that adds, updates, moves or deletes one or more files, all or nothing. Format:\n" +
        "*** Begin Patch\n" +
        "*** Add File: <path>\n+<each line of the new file>\n" +
        "*** Update File: <path>\n*** Move to: <new path>   (optional)\n" +
        "@@ <optional line to seek to, e.g. a function signature>\n <context line>\n-<removed line>\n+<added line>\n" +
        "*** Delete File: <path>\n" +
        "*** End Patch\n" +
        "Give about 3 lines of context around each change; a hunk that ends at the end of the file is followed by '*** End of File'. " +
        "Files updated, deleted or overwritten must have been read first. If any hunk does not apply, no file is changed.";

    private const string Begin = "*** Begin Patch";
    private const string End = "*** End Patch";
    private const string AddPrefix = "*** Add File: ";
    private const string UpdatePrefix = "*** Update File: ";
    private const string DeletePrefix = "*** Delete File: ";
    private const string MovePrefix = "*** Move to: ";
    private const string EndOfFile = "*** End of File";

    public PatchTool(string cwd)
        : base("apply_patch", "apply_patch", Describe, ArgsSchema, ToolsJsonContext.Default.PatchArgs, cwd)
    {
    }

    /// <summary>Accept <c>patch</c> for <c>input</c>, and a bare string as the whole argument.</summary>
    public override JsonNode? PrepareArguments(JsonNode? arguments)
    {
        if (arguments is JsonValue value && value.TryGetValue<string>(out var text) && text.TrimStart().StartsWith(Begin, StringComparison.Ordinal))
            return new JsonObject { ["input"] = text };
        if (arguments is JsonObject obj && obj["input"] is null && obj["patch"] is JsonValue patch && patch.TryGetValue<string>(out var p))
        {
            obj["input"] = p;
            obj.Remove("patch");
        }
        return arguments;
    }

    internal enum OpKind { Add, Update, Delete }

    /// <summary>One hunk: an optional <c>@@</c> seek line and its ' '/'-'/'+' lines.</summary>
    internal sealed record Hunk(string? Header, List<(char Kind, string Text)> Lines, bool AtEndOfFile);

    internal sealed record FileOp(OpKind Kind, string Path, string? MoveTo, List<string> AddLines, List<Hunk> Hunks);

    private sealed record Plan(FileOp Op, string Abs, string? MoveAbs, byte[]? NewBytes, string OldText, string NewText);

    protected override Task<AgentToolResult> ExecuteAsync(
        string toolCallId, PatchArgs args, Action<AgentToolResult>? onUpdate, CancellationToken cancellationToken)
    {
        var ops = Parse(args.Input ?? "");
        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var op in ops)
        {
            foreach (var p in op.MoveTo is null ? new[] { op.Path } : new[] { op.Path, op.MoveTo })
            {
                var abs = Resolve(p);
                if (!seen.Add(FileMutationQueue.QueueKey(abs)))
                    throw new InvalidOperationException($"apply_patch: {p} appears more than once in the patch. Put all changes to one file in one Update File section.");
                paths.Add(abs);
            }
        }
        // Lock every file in one global order so two patches never wait on each other.
        paths.Sort((a, b) => string.CompareOrdinal(FileMutationQueue.QueueKey(a), FileMutationQueue.QueueKey(b)));
        return LockAll(paths, 0, () => ApplyAsync(ops, cancellationToken), cancellationToken);
    }

    private static Task<AgentToolResult> LockAll(List<string> paths, int index, Func<Task<AgentToolResult>> action, CancellationToken ct) =>
        index == paths.Count ? action() : FileMutationQueue.RunAsync(paths[index], () => LockAll(paths, index + 1, action, ct), ct);

    private async Task<AgentToolResult> ApplyAsync(List<FileOp> ops, CancellationToken cancellationToken)
    {
        // Phase 1: read, check and compute everything; nothing on disk changes here.
        var plans = new List<Plan>(ops.Count);
        foreach (var op in ops)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var abs = Resolve(op.Path);
            var exists = File.Exists(abs);
            byte[]? current = exists ? await File.ReadAllBytesAsync(abs, cancellationToken).ConfigureAwait(false) : null;
            switch (op.Kind)
            {
                case OpKind.Add:
                {
                    if (current is not null) Guard(op, abs, current);
                    var text = op.AddLines.Count == 0 ? "" : string.Join('\n', op.AddLines) + "\n";
                    var old = current is null ? "" : EditDiff.NormalizeToLf(EditDiff.SplitBom(Encoding.UTF8.GetString(current)).Text);
                    plans.Add(new Plan(op, abs, null, WriteTool.Utf8NoBom.GetBytes(text), old, text));
                    break;
                }
                case OpKind.Delete:
                {
                    if (current is null) throw Failure(op, $"cannot delete {op.Path}: file not found.");
                    Guard(op, abs, current);
                    var old = EditDiff.NormalizeToLf(EditDiff.SplitBom(Encoding.UTF8.GetString(current)).Text);
                    plans.Add(new Plan(op, abs, null, null, old, ""));
                    break;
                }
                case OpKind.Update:
                {
                    if (current is null) throw Failure(op, $"cannot update {op.Path}: file not found.");
                    Guard(op, abs, current);
                    string? moveAbs = null;
                    if (op.MoveTo is not null)
                    {
                        moveAbs = Resolve(op.MoveTo);
                        if (File.Exists(moveAbs))
                            Guard(op, moveAbs, await File.ReadAllBytesAsync(moveAbs, cancellationToken).ConfigureAwait(false), op.MoveTo);
                    }
                    var (bom, content) = EditDiff.SplitBom(Encoding.UTF8.GetString(current));
                    var ending = EditDiff.DetectLineEnding(content);
                    var normalized = EditDiff.NormalizeToLf(content);
                    var updated = ApplyHunks(op, normalized);
                    var bytes = WriteTool.Utf8NoBom.GetBytes(bom + EditDiff.RestoreLineEndings(updated, ending));
                    plans.Add(new Plan(op, abs, moveAbs, bytes, normalized, updated));
                    break;
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Phase 2: write. Past this point cancellation is not observed; an I/O failure rolls back.
        var undo = new List<(string Path, byte[]? Original)>();
        try
        {
            foreach (var plan in plans)
            {
                var target = plan.MoveAbs ?? plan.Abs;
                if (plan.NewBytes is not null)
                {
                    undo.Add((target, File.Exists(target) ? await File.ReadAllBytesAsync(target, CancellationToken.None).ConfigureAwait(false) : null));
                    var dir = Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    await File.WriteAllBytesAsync(target, plan.NewBytes, CancellationToken.None).ConfigureAwait(false);
                }
                if (plan.NewBytes is null || plan.MoveAbs is not null)
                {
                    undo.Add((plan.Abs, await File.ReadAllBytesAsync(plan.Abs, CancellationToken.None).ConfigureAwait(false)));
                    File.Delete(plan.Abs);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            for (var i = undo.Count - 1; i >= 0; i--)
            {
                try
                {
                    if (undo[i].Original is { } original) File.WriteAllBytes(undo[i].Path, original);
                    else if (File.Exists(undo[i].Path)) File.Delete(undo[i].Path);
                }
                catch (Exception)
                {
                    // Best effort: keep restoring the rest.
                }
            }
            throw new InvalidOperationException($"apply_patch: writing failed ({ex.Message}); the files written so far were restored.", ex);
        }

        foreach (var plan in plans)
        {
            if (plan.NewBytes is null || plan.MoveAbs is not null) ReadTracker?.Forget(plan.Abs);
            if (plan.NewBytes is not null) ReadTracker?.Record(plan.MoveAbs ?? plan.Abs, plan.NewBytes);
        }

        return Result(plans);
    }

    private void Guard(FileOp op, string abs, byte[] current, string? display = null)
    {
        if (ReadTracker?.Check(abs, current, display ?? op.Path) is { } reason) throw Failure(op, reason);
    }

    private static AgentToolResult Result(List<Plan> plans)
    {
        var summary = new StringBuilder("Success. Updated the following files:");
        var diffs = new List<string>();
        var patches = new StringBuilder();
        int? firstChangedLine = null;
        foreach (var plan in plans)
        {
            var display = plan.Op.MoveTo ?? plan.Op.Path;
            var letter = plan.Op.Kind switch { OpKind.Add => 'A', OpKind.Delete => 'D', _ => 'M' };
            summary.Append('\n').Append(letter).Append(' ').Append(display);
            if (plan.Op.MoveTo is not null) summary.Append(" (moved from ").Append(plan.Op.Path).Append(')');

            if (plan.OldText == plan.NewText) continue;
            var diff = EditDiff.GenerateDiffString(plan.OldText, plan.NewText);
            diffs.Add(plans.Count == 1 ? diff.Diff : $"{letter} {display}\n{diff.Diff}");
            if (plans.Count == 1) firstChangedLine = diff.FirstChangedLine;
            patches.Append(LineDiff.UnifiedPatch(display, plan.OldText, plan.NewText));
        }

        var details = new JsonObject
        {
            ["diff"] = string.Join("\n\n", diffs),
            ["patch"] = patches.ToString(),
        };
        if (firstChangedLine is { } line) details["firstChangedLine"] = line;
        return AgentToolResult.FromText(summary.ToString(), details);
    }

    // ---- parsing ---------------------------------------------------------------------------------------

    /// <summary>Parse a patch into file operations, or throw with the line that is wrong.</summary>
    internal static List<FileOp> Parse(string input)
    {
        var lines = input.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        while (lines.Count > 0 && lines[^1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);
        while (lines.Count > 0 && lines[0].Trim().Length == 0) lines.RemoveAt(0);
        // A shell heredoc wrapper (apply_patch <<'EOF' ... EOF) is common in Codex transcripts.
        if (lines.Count >= 2 && lines[0].TrimStart().StartsWith("<<", StringComparison.Ordinal) && lines[^1].Trim() is "EOF")
            lines = lines[1..^1];

        if (lines.Count < 2 || lines[0].Trim() != Begin)
            throw Invalid($"the first line must be '{Begin}'.");
        if (lines[^1].Trim() != End)
            throw Invalid($"the last line must be '{End}'.");

        var ops = new List<FileOp>();
        var i = 1;
        var last = lines.Count - 1;
        while (i < last)
        {
            var header = lines[i];
            if (header.StartsWith(AddPrefix, StringComparison.Ordinal))
            {
                var path = PathOf(header, AddPrefix, i);
                i++;
                var add = new List<string>();
                var trailingBlank = 0;
                while (i < last && !IsFileHeader(lines[i]))
                {
                    var line = lines[i];
                    if (line.StartsWith('+')) { add.Add(line[1..]); trailingBlank = 0; }
                    else if (line.Trim().Length == 0) { add.Add(""); trailingBlank++; }
                    else throw Invalid($"line {i + 1}: every line of an added file starts with '+', got '{Clip(line)}'.");
                    i++;
                }
                // Bare blank lines before the next header are layout, not content.
                add.RemoveRange(add.Count - trailingBlank, trailingBlank);
                ops.Add(new FileOp(OpKind.Add, path, null, add, []));
            }
            else if (header.StartsWith(DeletePrefix, StringComparison.Ordinal))
            {
                ops.Add(new FileOp(OpKind.Delete, PathOf(header, DeletePrefix, i), null, [], []));
                i++;
                while (i < last && lines[i].Trim().Length == 0) i++;
            }
            else if (header.StartsWith(UpdatePrefix, StringComparison.Ordinal))
            {
                var path = PathOf(header, UpdatePrefix, i);
                i++;
                string? moveTo = null;
                if (i < last && lines[i].StartsWith(MovePrefix, StringComparison.Ordinal))
                {
                    moveTo = PathOf(lines[i], MovePrefix, i);
                    i++;
                }
                var hunks = new List<Hunk>();
                Hunk? hunk = null;
                while (i < last && !IsFileHeader(lines[i]))
                {
                    var line = lines[i];
                    if (line.StartsWith("@@", StringComparison.Ordinal))
                    {
                        var seek = line[2..].Trim();
                        if (seek.EndsWith("@@", StringComparison.Ordinal)) seek = "";
                        hunk = new Hunk(seek.Length == 0 ? null : seek, [], false);
                        hunks.Add(hunk);
                    }
                    else if (line.Trim() == EndOfFile)
                    {
                        if (hunk is null) throw Invalid($"line {i + 1}: '{EndOfFile}' before any hunk in {path}.");
                        hunks[^1] = hunk = hunk with { AtEndOfFile = true };
                    }
                    else
                    {
                        if (hunk is null)
                        {
                            if (line.Length == 0 || line[0] is not (' ' or '-' or '+'))
                                throw Invalid($"line {i + 1}: expected an '@@' hunk in {path}, got '{Clip(line)}'.");
                            hunk = new Hunk(null, [], false);
                            hunks.Add(hunk);
                        }
                        if (line.Length == 0) hunk.Lines.Add((' ', ""));
                        else if (line[0] is ' ' or '-' or '+') hunk.Lines.Add((line[0], line[1..]));
                        else throw Invalid($"line {i + 1}: hunk lines start with ' ', '-' or '+', got '{Clip(line)}'.");
                    }
                    i++;
                }
                for (var h = 0; h < hunks.Count; h++)
                {
                    if (!hunks[h].Lines.Any(l => l.Kind is '-' or '+'))
                        throw Invalid($"{path}, hunk {h + 1}: the hunk changes nothing.");
                }
                if (hunks.Count == 0 && moveTo is null) throw Invalid($"Update File {path} has no hunks.");
                ops.Add(new FileOp(OpKind.Update, path, moveTo, [], hunks));
            }
            else if (header.Trim().Length == 0)
            {
                i++;
            }
            else
            {
                throw Invalid($"line {i + 1}: expected '*** Add File:', '*** Update File:' or '*** Delete File:', got '{Clip(header)}'.");
            }
        }

        if (ops.Count == 0) throw Invalid("the patch contains no file operations.");
        return ops;
    }

    private static bool IsFileHeader(string line) =>
        line.StartsWith(AddPrefix, StringComparison.Ordinal)
        || line.StartsWith(UpdatePrefix, StringComparison.Ordinal)
        || line.StartsWith(DeletePrefix, StringComparison.Ordinal);

    private static string PathOf(string line, string prefix, int index)
    {
        var path = line[prefix.Length..].Trim();
        if (path.Length == 0) throw Invalid($"line {index + 1}: '{prefix.Trim()}' needs a path.");
        return path;
    }

    // ---- applying --------------------------------------------------------------------------------------

    /// <summary>Apply an Update section's hunks to LF-normalized text, or throw naming the failing hunk.</summary>
    internal static string ApplyHunks(FileOp op, string normalized)
    {
        var hadTrailingNewline = normalized.EndsWith('\n');
        var lines = normalized.Length == 0 ? new List<string>() : normalized.Split('\n').ToList();
        if (hadTrailingNewline) lines.RemoveAt(lines.Count - 1);

        var replacements = new List<(int Start, int Count, List<string> NewLines)>();
        var cursor = 0;
        for (var h = 0; h < op.Hunks.Count; h++)
        {
            var hunk = op.Hunks[h];
            string Where() => $"hunk {h + 1} of {op.Hunks.Count}";
            if (hunk.Header is { } seek)
            {
                // The @@ line only locates; models write it without its indentation, so it matches trimmed.
                var found = lines.FindIndex(cursor, l => l.Trim() == seek);
                if (found < 0) throw Failure(op, $"{Where()}: the '@@ {Clip(seek)}' line was not found{After(cursor)}.");
                cursor = found + 1;
            }

            var hunkLines = hunk.Lines;
            var old = hunkLines.Where(l => l.Kind is ' ' or '-').Select(l => l.Text).ToList();
            if (old.Count == 0)
            {
                // Pure insertion: after the @@ line when there is one, else at the end of the file.
                var at = hunk.Header is null ? lines.Count : cursor;
                replacements.Add((at, 0, hunkLines.Select(l => l.Text).ToList()));
                cursor = at;
                continue;
            }

            var start = Seek(lines, old, cursor, hunk.AtEndOfFile);
            if (start < 0 && hunkLines[^1] is (' ', ""))
            {
                // A blank context line at the end is usually the gap before the next section, not file content.
                hunkLines = hunkLines.Take(hunkLines.Count - 1).ToList();
                while (hunkLines.Count > 0 && hunkLines[^1] is (' ', "")) hunkLines.RemoveAt(hunkLines.Count - 1);
                old = hunkLines.Where(l => l.Kind is ' ' or '-').Select(l => l.Text).ToList();
                start = old.Count == 0 ? -1 : Seek(lines, old, cursor, hunk.AtEndOfFile);
            }
            if (start < 0)
            {
                var preview = string.Join(" | ", old.Take(3).Select(Clip));
                throw Failure(op, $"{Where()}: its context and '-' lines were not found{After(cursor)} (they begin: {preview}). Read the file again and copy the lines exactly.");
            }

            // Context lines keep the file's own bytes, so a whitespace-insensitive match rewrites nothing it only matched.
            var replacement = new List<string>();
            var j = start;
            foreach (var (kind, text) in hunkLines)
            {
                if (kind == ' ') replacement.Add(lines[j++]);
                else if (kind == '-') j++;
                else replacement.Add(text);
            }
            replacements.Add((start, old.Count, replacement));
            cursor = start + old.Count;
        }

        for (var r = replacements.Count - 1; r >= 0; r--)
        {
            var (start, count, newLines) = replacements[r];
            lines.RemoveRange(start, count);
            lines.InsertRange(start, newLines);
        }

        if (lines.Count == 0) return "";
        return string.Join('\n', lines) + (hadTrailingNewline || normalized.Length == 0 ? "\n" : "");
    }

    /// <summary>First index at or after <paramref name="from"/> where <paramref name="pattern"/> matches, in Codex's passes:
    /// exact, trailing-whitespace-insensitive, surrounding-whitespace-insensitive, then with typographic punctuation read as
    /// ASCII. The first pass that matches anywhere wins.</summary>
    private static int Seek(List<string> lines, List<string> pattern, int from, bool atEndOfFile)
    {
        for (var loose = 0; loose <= 3; loose++)
        {
            if (atEndOfFile)
            {
                var end = lines.Count - pattern.Count;
                if (end >= from && MatchesAt(lines, pattern, end, loose)) return end;
            }
            for (var i = from; i + pattern.Count <= lines.Count; i++)
            {
                if (MatchesAt(lines, pattern, i, loose)) return i;
            }
        }
        return -1;
    }

    private static bool MatchesAt(List<string> lines, List<string> pattern, int at, int loose)
    {
        for (var k = 0; k < pattern.Count; k++)
        {
            var a = lines[at + k];
            var b = pattern[k];
            var same = loose switch
            {
                0 => a == b,
                1 => a.AsSpan().TrimEnd().SequenceEqual(b.AsSpan().TrimEnd()),
                2 => a.AsSpan().Trim().SequenceEqual(b.AsSpan().Trim()),
                _ => Plain(a) == Plain(b),
            };
            if (!same) return false;
        }
        return true;
    }

    /// <summary>A line trimmed, with typographic dashes, quotes and spaces read as their ASCII forms (Codex's normalise).</summary>
    internal static string Plain(string line) => new([.. line.Trim().Select(c => c switch
    {
        '\u2010' or '\u2011' or '\u2012' or '\u2013' or '\u2014' or '\u2015' or '\u2212' => '-',
        '\u2018' or '\u2019' or '\u201A' or '\u201B' => '\'',
        '\u201C' or '\u201D' or '\u201E' or '\u201F' => '"',
        '\u00A0' or '\u2002' or '\u2003' or '\u2004' or '\u2005' or '\u2006' or '\u2007' or '\u2008' or '\u2009'
            or '\u200A' or '\u202F' or '\u205F' or '\u3000' => ' ',
        _ => c,
    })]);

    private static string After(int cursor) => cursor == 0 ? "" : $" after line {cursor}";

    private static string Clip(string text) => text.Length <= 80 ? text : text[..77] + "...";

    private static InvalidOperationException Invalid(string reason) =>
        new($"apply_patch: invalid patch, nothing was changed: {reason}");

    private static InvalidOperationException Failure(FileOp op, string reason)
    {
        var verb = op.Kind switch { OpKind.Add => "Add File", OpKind.Delete => "Delete File", _ => "Update File" };
        return new($"apply_patch failed, nothing was changed. {verb} {op.Path}: {reason}");
    }
}
