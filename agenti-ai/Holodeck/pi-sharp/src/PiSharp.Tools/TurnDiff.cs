// plan-pi-sharp-codex item 19 (Codex's TurnDiffTracker): what one turn changed in the files, kept as a unified diff. Before a
// batch that edits files runs, each file it names is read once (its content before the turn); when the turn ends, each is
// diffed against what is on disk now. Like Codex it sees the file tools (edit, write, apply_patch), not what a shell
// command changed; the checkpoints cover those.
using System.Text;
using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Messages;

namespace PiSharp.Tools;

/// <summary>One file's change over a turn: lines added and removed; <see cref="Created"/>/<see cref="Deleted"/> when it
/// did not exist before or does not now.</summary>
public sealed record TurnFileChange(string Path, int Added, int Removed, bool Created = false, bool Deleted = false);

/// <summary>A turn's changes: per file, the totals, and the unified diff cut to its cap.</summary>
public sealed record TurnDiffResult(IReadOnlyList<TurnFileChange> Files, string Diff, bool Truncated)
{
    public int Added => Files.Sum(f => f.Added);
    public int Removed => Files.Sum(f => f.Removed);

    /// <summary>One line for the seat log: "3 files (+40 -12): a.cs, b.cs, c.cs".</summary>
    public string Line() =>
        $"{Files.Count} file{(Files.Count == 1 ? "" : "s")} (+{Added} -{Removed}): {string.Join(", ", Files.Take(8).Select(f => f.Path))}{(Files.Count > 8 ? ", ..." : "")}";

    public JsonObject ToJson() => new()
    {
        ["files"] = new JsonArray([.. Files.Select(f =>
        {
            var o = new JsonObject { ["path"] = f.Path, ["added"] = f.Added, ["removed"] = f.Removed };
            if (f.Created) o["created"] = true;
            if (f.Deleted) o["deleted"] = true;
            return (JsonNode)o;
        })]),
        ["added"] = Added,
        ["removed"] = Removed,
        ["diff"] = Diff,
        ["truncated"] = Truncated,
    };
}

public sealed class TurnDiffTracker(string cwd)
{
    /// <summary>A file larger than this is listed as changed without lines (the diff would cost more than it tells).</summary>
    public const int MaxFileBytes = 1024 * 1024;
    public const int DefaultMaxDiffBytes = 32 * 1024;

    private readonly Dictionary<string, string?> _before = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public int Tracked { get { lock (_gate) return _before.Count; } }

    /// <summary>Reads, once per turn, every file the batch's file tools will write, before they run.</summary>
    public void Capture(IReadOnlyList<ToolCallContent> calls, IReadOnlyList<IAgentTool>? tools = null)
    {
        foreach (var call in calls)
            foreach (var path in WrittenPaths(call, tools))
                lock (_gate)
                    if (!_before.ContainsKey(path)) _before[path] = ReadOrNull(path);
    }

    /// <summary>The files a call writes: edit/write name theirs in <c>path</c> (their <see cref="ToolAccess"/>), apply_patch in
    /// its Add/Update/Delete/Move headers.</summary>
    public IEnumerable<string> WrittenPaths(ToolCallContent call, IReadOnlyList<IAgentTool>? tools = null)
    {
        if (call.Name == "apply_patch")
        {
            var text = call.Arguments["input"]?.GetValue<string>() ?? call.Arguments["patch"]?.GetValue<string>() ?? "";
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                foreach (var prefix in PatchPrefixes)
                    if (line.StartsWith(prefix, StringComparison.Ordinal) && line[prefix.Length..].Trim() is { Length: > 0 } p)
                        yield return ToolPaths.ResolveToCwd(p, cwd);
            }
            yield break;
        }
        if (call.Name is not ("edit" or "write")) yield break;
        var access = tools?.FirstOrDefault(t => t.Name == call.Name)?.Access(call.Arguments);
        if (access is { ReadOnly: false })
            foreach (var r in access.Resources) yield return r;
        else if (access is null && call.Arguments["path"]?.GetValue<string>() is { Length: > 0 } path)
            yield return ToolPaths.ResolveToCwd(path, cwd);
    }

    private static readonly string[] PatchPrefixes = ["*** Add File: ", "*** Update File: ", "*** Delete File: ", "*** Move to: "];

    /// <summary>The turn's changes, or null when no tracked file changed; the tracker starts the next turn empty.</summary>
    public TurnDiffResult? Finish(int maxDiffBytes = DefaultMaxDiffBytes)
    {
        KeyValuePair<string, string?>[] before;
        lock (_gate) { before = [.. _before.OrderBy(kv => kv.Key, StringComparer.Ordinal)]; _before.Clear(); }
        var files = new List<TurnFileChange>();
        var diff = new StringBuilder();
        var truncated = false;
        foreach (var (path, old) in before)
        {
            var now = ReadOrNull(path);
            if (old == now) continue;
            var shown = Relative(path);
            if (IsLarge(old) || IsLarge(now))
            {
                files.Add(new(shown, 0, 0, old is null, now is null));
                continue;
            }
            int added = 0, removed = 0;
            foreach (var part in LineDiff.Diff(old ?? "", now ?? ""))
                if (part.Op == DiffOp.Insert) added += part.Lines.Count;
                else if (part.Op == DiffOp.Delete) removed += part.Lines.Count;
            files.Add(new(shown, added, removed, old is null, now is null));
            if (truncated) continue;
            var patch = LineDiff.UnifiedPatch(shown, old ?? "", now ?? "");
            if (diff.Length + patch.Length > maxDiffBytes) { truncated = true; continue; }
            diff.Append(patch);
            if (!patch.EndsWith('\n')) diff.Append('\n');
        }
        return files.Count == 0 ? null : new TurnDiffResult(files, diff.ToString(), truncated);
    }

    private string Relative(string path)
    {
        var relative = Path.GetRelativePath(cwd, path);
        return relative.StartsWith("..", StringComparison.Ordinal) ? path : relative.Replace('\\', '/');
    }

    private const string LargeMarker = "\0large:";
    private static bool IsLarge(string? content) => content?.StartsWith(LargeMarker, StringComparison.Ordinal) == true;

    private static string? ReadOrNull(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return null;
            // Past the cap the file is listed, not diffed: a marker that changes with its size and its time.
            return info.Length > MaxFileBytes ? FormattableString.Invariant($"{LargeMarker}{info.Length}@{info.LastWriteTimeUtc.Ticks}") : File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
}
