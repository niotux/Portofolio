// Hand-written .gitignore matcher for managed grep/find (decision 5: no rg/fd).
// Always skips .git; includes hidden files; nested repositories reset parent rules
// (fd's git-aware behavior, https://github.com/earendil-works/pi/issues/5960).
using System.Text.RegularExpressions;

namespace PiSharp.Tools;

/// <summary>One gitignore pattern, compiled relative to the directory that contained it.</summary>
public sealed class GitIgnoreRule
{
    public required bool Negate { get; init; }
    public required bool DirectoryOnly { get; init; }
    public required Regex Regex { get; init; }
    public required string Pattern { get; init; }
}

/// <summary>Parsed gitignore rules for one directory.</summary>
public sealed class GitIgnoreFile
{
    public required string Directory { get; init; }
    public required IReadOnlyList<GitIgnoreRule> Rules { get; init; }
    public bool HasNegation { get; init; }
}

/// <summary>Walk a tree applying .gitignore, skipping .git, including hidden files.</summary>
public static class GitIgnore
{
    public const string GitDirName = ".git";

    /// <summary>Parse the contents of a <c>.gitignore</c> file.</summary>
    public static IReadOnlyList<GitIgnoreRule> Parse(string contents)
    {
        var rules = new List<GitIgnoreRule>();
        foreach (var rawLine in contents.Replace("\r\n", "\n").Split('\n'))
        {
            var line = UnescapeTrailingSpaces(rawLine);
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var negate = false;
            if (line.StartsWith('!'))
            {
                negate = true;
                line = line[1..];
                if (line.Length == 0) continue;
            }

            var directoryOnly = line.EndsWith('/');
            var pattern = directoryOnly ? line[..^1] : line;
            if (pattern.Length == 0) continue;

            rules.Add(new GitIgnoreRule
            {
                Negate = negate,
                DirectoryOnly = directoryOnly,
                Regex = Glob.ToRegex(directoryOnly ? pattern + "/" : pattern),
                Pattern = line,
            });
        }
        return rules;
    }

    /// <summary>
    /// Walk <paramref name="root"/> and yield files (and optionally directories) that are
    /// not ignored. Always skips <c>.git</c>. Does not follow directory symlinks.
    /// </summary>
    public static IEnumerable<string> WalkFiles(string root, CancellationToken cancellationToken = default)
    {
        foreach (var (path, isDir) in Walk(root, cancellationToken))
        {
            if (!isDir) yield return path;
        }
    }

    /// <summary>Walk files and directories. Directory paths do not have a trailing slash.</summary>
    public static IEnumerable<(string Path, bool IsDirectory)> Walk(string root, CancellationToken cancellationToken = default)
    {
        root = Path.GetFullPath(root);
        var stack = new Stack<(string Dir, List<GitIgnoreFile> Rules)>();
        stack.Push((root, LoadRulesTowardRoot(root)));

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (dir, inherited) = stack.Pop();

            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(dir);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            var children = entries.ToList();
            // Stable, case-insensitive order so grep/find output is deterministic like rg/fd.
            children.Sort(StringComparer.OrdinalIgnoreCase);
            // Depth-first with a stack: push in reverse so we visit in sorted order.
            for (var i = children.Count - 1; i >= 0; i--)
            {
                var entry = children[i];
                var name = Path.GetFileName(entry);
                if (name == GitDirName) continue;

                bool isDir;
                bool isLink;
                try
                {
                    var attr = File.GetAttributes(entry);
                    isDir = (attr & FileAttributes.Directory) != 0;
                    isLink = (attr & FileAttributes.ReparsePoint) != 0;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                if (isDir && isLink) continue;

                var relativeToRoot = ToolPaths.RelativePosix(root, entry);
                if (relativeToRoot.StartsWith("..", StringComparison.Ordinal)) continue;

                var ignored = IsIgnored(relativeToRoot, isDir, root, inherited);
                List<GitIgnoreFile>? childRules = null;
                if (isDir)
                {
                    childRules = inherited;
                    if (Directory.Exists(Path.Combine(entry, GitDirName)))
                    {
                        // Nested repository: parent gitignore no longer applies.
                        childRules = [];
                    }
                    if (File.Exists(Path.Combine(entry, ".gitignore")))
                    {
                        childRules = [.. childRules, LoadIgnoreFile(entry)];
                    }
                }

                if (ignored)
                {
                    if (!isDir) continue;
                    if (childRules is null || !childRules.Any(f => f.HasNegation) && !inherited.Any(f => f.HasNegation))
                    {
                        continue;
                    }
                    // A negation might un-ignore a child; keep walking but do not yield the dir.
                    stack.Push((entry, childRules));
                    continue;
                }

                yield return (entry, isDir);
                if (isDir) stack.Push((entry, childRules!));
            }
        }
    }

    /// <summary>Whether <paramref name="relativePosix"/> is ignored by the stacked rules.</summary>
    public static bool IsIgnored(string relativePosix, bool isDirectory, string walkRoot, IReadOnlyList<GitIgnoreFile> rules)
    {
        relativePosix = relativePosix.Replace('\\', '/');
        if (relativePosix is "." or "") return false;

        var ignored = false;
        foreach (var file in rules)
        {
            var relToFile = Relativize(walkRoot, file.Directory, relativePosix);
            if (relToFile is null) continue;
            foreach (var rule in file.Rules)
            {
                if (rule.DirectoryOnly && !isDirectory) continue;
                var candidate = isDirectory && !relToFile.EndsWith('/') ? relToFile + "/" : relToFile;
                if (rule.Regex.IsMatch(relToFile) || (isDirectory && rule.Regex.IsMatch(candidate)))
                {
                    ignored = !rule.Negate;
                }
            }
        }
        return ignored;
    }

    internal static GitIgnoreFile LoadIgnoreFile(string directory)
    {
        var path = Path.Combine(directory, ".gitignore");
        IReadOnlyList<GitIgnoreRule> rules;
        try
        {
            rules = Parse(File.ReadAllText(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            rules = [];
        }
        return new GitIgnoreFile
        {
            Directory = Path.GetFullPath(directory),
            Rules = rules,
            HasNegation = rules.Any(r => r.Negate),
        };
    }

    /// <summary>Collect .gitignore files from the git root (or filesystem root) down to <paramref name="dir"/>.</summary>
    internal static List<GitIgnoreFile> LoadRulesTowardRoot(string dir)
    {
        dir = Path.GetFullPath(dir);
        var chain = new List<string>();
        var current = dir;
        string? gitRoot = null;
        while (true)
        {
            chain.Add(current);
            if (Directory.Exists(Path.Combine(current, GitDirName)) || File.Exists(Path.Combine(current, GitDirName)))
            {
                gitRoot = current;
                break;
            }
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || parent == current) break;
            current = parent;
        }

        chain.Reverse();
        if (gitRoot is not null)
        {
            var rootFull = Path.GetFullPath(gitRoot);
            chain = chain.Where(p => Path.GetFullPath(p).StartsWith(rootFull, StringComparison.Ordinal)).ToList();
        }

        var files = new List<GitIgnoreFile>();
        foreach (var d in chain)
        {
            if (File.Exists(Path.Combine(d, ".gitignore"))) files.Add(LoadIgnoreFile(d));
        }
        return files;
    }

    private static string? Relativize(string walkRoot, string ignoreDir, string relativeToWalkRoot)
    {
        var full = Path.GetFullPath(Path.Combine(walkRoot, relativeToWalkRoot.Replace('/', Path.DirectorySeparatorChar)));
        var ignoreFull = Path.GetFullPath(ignoreDir);
        var rel = Path.GetRelativePath(ignoreFull, full).Replace('\\', '/');
        if (rel.StartsWith("..", StringComparison.Ordinal)) return null;
        return rel == "." ? "" : rel;
    }

    private static string UnescapeTrailingSpaces(string line)
    {
        // gitignore: trailing spaces are ignored unless escaped with a backslash.
        if (line.Length == 0) return line;
        var end = line.Length;
        while (end > 0 && line[end - 1] == ' ')
        {
            if (end >= 2 && line[end - 2] == '\\') break;
            end--;
        }
        return line[..end].Replace("\\ ", " ", StringComparison.Ordinal);
    }
}
