using System.Security.Cryptography;
using System.Text;

namespace PiSharp.Core.Notebook;

/// <summary>One-off move of working memory that seats kept as .md files (work/*.md) into the notebook, as notes keyed
/// <c>file:&lt;relative path&gt;</c>. Idempotent: an unchanged file is a no-op write, so running it twice appends
/// nothing; a changed file becomes a new revision of the same note.</summary>
public static class NotebookImport
{
    public sealed record Result(int Imported, int Updated, int Unchanged, IReadOnlyList<string> Skipped);

    /// <summary>Imports every *.md under <paramref name="directory"/> (recursively). Keys are relative to
    /// <paramref name="baseDirectory"/> (default: the current directory), so <c>work/plan.md</c> becomes
    /// <c>file:work:plan.md</c> (notebook IDs cannot hold a slash; a colon stands for it).</summary>
    public static Result Run(NotebookStore store, string directory, string? baseDirectory = null, Action<string>? report = null)
    {
        var root = Path.GetFullPath(directory);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"No such directory: {directory}");
        var baseDir = Path.GetFullPath(baseDirectory ?? Directory.GetCurrentDirectory());
        int imported = 0, updated = 0, unchanged = 0;
        var skipped = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(baseDir, file).Replace('\\', '/');
            if (relative.StartsWith("../", StringComparison.Ordinal)) relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            var bytes = File.ReadAllBytes(file);
            if (bytes.Length > NotebookStore.MaxRecordBytes)
            {
                skipped.Add(relative + " (over 256 KiB)");
                report?.Invoke($"skipped {relative}: over 256 KiB; keep it as a file or split it");
                continue;
            }
            var text = new UTF8Encoding(false).GetString(bytes).TrimStart('﻿');
            var key = Key(relative);
            var existed = store.Get(key) is not null;
            try
            {
                var (_, changed) = store.Write(key, text, Title(text, relative), ["imported"], kind: "note");
                if (!changed) unchanged++;
                else if (existed) updated++;
                else imported++;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                skipped.Add(relative + " (" + ex.Message + ")");
                report?.Invoke($"skipped {relative}: {ex.Message}");
            }
        }
        return new Result(imported, updated, unchanged, skipped);
    }

    /// <summary><c>file:</c> plus the relative path, slashes as colons, other characters an ID cannot hold as
    /// underscores. Over 160 characters, the tail is replaced by a hash so two long paths stay two keys.</summary>
    public static string Key(string relativePath)
    {
        var body = new StringBuilder("file:");
        foreach (var c in relativePath.Replace('\\', '/'))
            body.Append(c == '/' ? ':' : char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_');
        var key = body.ToString();
        if (key.Length <= 160) return key;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(relativePath)))[..12].ToLowerInvariant();
        return key[..(160 - 13)] + "-" + hash;
    }

    /// <summary>The first Markdown heading, else the file name.</summary>
    public static string Title(string text, string relativePath)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith('#')) continue;
            var title = line.TrimStart('#').Trim();
            if (title.Length > 0) return title.Length > NotebookStore.MaxTitleLength ? title[..NotebookStore.MaxTitleLength] : title;
        }
        return Path.GetFileName(relativePath);
    }
}
