// Ported from pi: packages/coding-agent/src/core/tools/path-utils.ts and
// packages/coding-agent/src/utils/paths.ts (normalizePath, resolvePath) @ 71dca871b
//
// Dropped from the port: pi's macOS filename fallbacks in resolveReadPath (narrow no-break
// space before AM/PM, NFD, U+2019 curly quote). pi-sharp is Linux-only with a few Windows
// exceptions and never runs on macOS (decision 13), so macOS-specific code is not ported.
using System.Text.RegularExpressions;

namespace PiSharp.Tools;

/// <summary>Path normalization shared by the built-in tools.</summary>
public static partial class ToolPaths
{
    /// <summary>Unicode spaces models copy out of terminals or documents, normalized to U+0020.</summary>
    [GeneratedRegex("[  -   　]")]
    private static partial Regex UnicodeSpaces();

    /// <summary>Git Bash / MSYS / Cygwin / WSL drive prefixes, e.g. <c>/d/code</c> or <c>/mnt/d/code</c>.</summary>
    [GeneratedRegex(@"^/(?:mnt/|cygdrive/)?([a-zA-Z])(?:/(.*))?$")]
    private static partial Regex ShellDrivePath();

    /// <summary>Convert a Git Bash / MSYS / Cygwin / WSL drive path to a native Windows path.</summary>
    public static string NormalizeWindowsShellPath(string filePath)
    {
        if (!OperatingSystem.IsWindows()) return filePath;
        if (!filePath.StartsWith('/') || filePath.StartsWith("//") || filePath.Contains('\\')) return filePath;
        var match = ShellDrivePath().Match(filePath);
        if (!match.Success) return filePath;
        var suffix = match.Groups[2].Success ? match.Groups[2].Value.Replace('/', '\\') : "";
        return $"{char.ToUpperInvariant(match.Groups[1].Value[0])}:\\{suffix}";
    }

    /// <summary>Normalize unicode spaces, strip a leading <c>@</c>, expand <c>~</c> and Windows shell paths.</summary>
    public static string ExpandPath(string filePath)
    {
        var normalized = UnicodeSpaces().Replace(filePath, " ");
        if (normalized.StartsWith('@')) normalized = normalized[1..];
        normalized = NormalizeWindowsShellPath(normalized);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (normalized == "~") return home;
        if (normalized.StartsWith("~/") || (OperatingSystem.IsWindows() && normalized.StartsWith("~\\")))
        {
            return Path.Combine(home, normalized[2..]);
        }
        return normalized;
    }

    /// <summary>Resolve a tool path argument against the agent's working directory.</summary>
    public static string ResolveToCwd(string filePath, string cwd)
    {
        var normalized = ExpandPath(filePath);
        return Path.GetFullPath(normalized, Path.GetFullPath(cwd));
    }

    /// <summary>Path of <paramref name="filePath"/> relative to <paramref name="baseDir"/>, with forward slashes.</summary>
    public static string RelativePosix(string baseDir, string filePath)
    {
        var relative = Path.GetRelativePath(baseDir, filePath);
        return relative.Replace('\\', '/');
    }
}
