// Ported from pi: packages/coding-agent/src/core/resource-loader.ts (AGENTS.md discovery) @ 71dca871b
// Dropped: git-worktree shadowing (macOS/worktree edge case).
namespace PiSharp.Core.Config;

public sealed record ContextFile(string Path, string Content);

/// <summary>Discovers AGENTS.md / CLAUDE.md from the global config dir, cwd, and parent directories.</summary>
public static class ContextFiles
{
    private static readonly string[] Candidates =
        ["AGENTS.override.md", "AGENTS.md", "AGENTS.MD", "CLAUDE.md", "CLAUDE.MD"];

    public static IReadOnlyList<ContextFile> Load(string cwd, string? globalDir = null)
    {
        var resolvedCwd = Path.GetFullPath(cwd);
        var files = new List<ContextFile>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void TryAdd(ContextFile? file)
        {
            if (file is null) return;
            string key;
            try { key = Path.GetFullPath(file.Path); }
            catch { key = file.Path; }
            if (!seen.Add(key)) return;
            files.Add(file);
        }

        TryAdd(LoadFromDir(ConfigPaths.GlobalConfigDir(globalDir)));

        var ancestors = new List<ContextFile>();
        var current = resolvedCwd;
        while (true)
        {
            var file = LoadFromDir(current);
            if (file is not null && seen.Add(Path.GetFullPath(file.Path)))
                ancestors.Insert(0, file);
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || parent == current) break;
            current = parent;
        }
        files.AddRange(ancestors);
        return files;
    }

    public static ContextFile? LoadFromDir(string dir)
    {
        foreach (var name in Candidates)
        {
            var path = Path.Combine(dir, name);
            if (!File.Exists(path)) continue;
            try
            {
                var content = File.ReadAllText(path);
                if (content.Length > 0 && content[0] == '\uFEFF') content = content[1..];
                return new ContextFile(Path.GetFullPath(path), content);
            }
            catch { /* unreadable, try next candidate */ }
        }
        return null;
    }
}
