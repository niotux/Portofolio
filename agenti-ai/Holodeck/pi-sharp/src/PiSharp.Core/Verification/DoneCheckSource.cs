using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PiSharp.Core.Verification;

/// <summary>Done-checks for a task from two spec locations, checked in order; the repo file wins because it is
/// versioned with the code:
/// 1. <c>.pi/done-checks.json</c> in the working directory: {"task": "...", "checks": [...]} or a bare list.
/// 2. A ```done-check fence in the work-item spec text: one JSON object per line.
/// Only the spec and the repo declare checks; the model's notebook and message text never do.</summary>
public sealed class DoneCheckSource(string cwd, Func<string, string>? specReader = null) : IDoneCheckSource
{
    public const string RepoFileName = ".pi/done-checks.json";

    private static readonly Regex Fence = new("```done-check\\s*\\n(.*?)```", RegexOptions.Singleline | RegexOptions.IgnoreCase);

    /// <summary>The task key the spec text belongs to; null when the text carries no fence.</summary>
    public static bool TryParseFence(string spec, out IReadOnlyList<DoneCheck> checks)
    {
        checks = [];
        var match = Fence.Match(spec);
        if (!match.Success) return false;
        foreach (var line in match.Groups[1].Value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.Length == 0 || line[0] is not ('{' or '[')) continue;
            JsonNode? node = null;
            try { node = JsonNode.Parse(line); } catch (JsonException) { continue; }
            foreach (var o in AsObjects(node))
            {
                var check = DoneCheck.Parse(o);
                if (check is not null) checks = [.. checks, check];
            }
        }
        return checks.Count > 0;
    }

    /// <summary>Checks for <paramref name name="task"/>: the repo file when it names the task (or names none),
    /// otherwise the fence in the spec the seat supplies via <paramref name="specReader"/>.</summary>
    public IReadOnlyList<DoneCheck> For(string task)
    {
        var file = TryReadRepoFile(task);
        if (file is not null) return file;
        var spec = specReader?.Invoke(task) ?? "";
        return TryParseFence(spec, out var checks) ? checks : [];
    }

    private static IEnumerable<JsonObject> AsObjects(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject o: yield return o; break;
            case JsonArray array: foreach (var item in array) if (item is JsonObject e) yield return e; break;
        }
    }

    private IReadOnlyList<DoneCheck>? TryReadRepoFile(string task)
    {
        var path = Path.Combine(cwd, RepoFileName.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path)) return null;
        JsonNode? root;
        try { root = JsonNode.Parse(File.ReadAllText(path)); }
        catch (JsonException) { return null; } // a broken file is reported by the checks, not by the source
        JsonObject? o = null;
        switch (root)
        {
            case JsonObject obj:
                // {"task": "...", "checks": [...]} — the file belongs to this task when it names one; a file with no
                // task key covers every task in this repo (a per-repo default).
                var named = obj["task"]?.GetValue<string>();
                if (named is not null && named != task) return null;
                o = obj;
                break;
            case JsonArray:
                o = null; // bare list covers every task
                break;
        }
        if (o is null && root is not JsonArray) return null;
        JsonNode? list = o?["checks"] ?? (o is null ? root! : null);
        if (list is not JsonArray array) return null;
        var checks = array.Select(n => n is JsonObject e ? DoneCheck.Parse(e) : null).Where(c => c is not null).Select(c => c!).ToArray();
        return checks;
    }
}
