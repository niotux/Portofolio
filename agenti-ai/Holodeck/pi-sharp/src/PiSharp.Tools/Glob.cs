// Hand-written glob matcher used by managed grep/find (decision 5: no rg/fd).
// fd semantics: a pattern without '/' matches the basename; a pattern with '/' matches
// the relative path, with an implicit '**/' prefix unless it already starts with '/' or '**/'.
using System.Text;
using System.Text.RegularExpressions;

namespace PiSharp.Tools;

/// <summary>gitignore-style glob matching for the find and grep tools.</summary>
public static class Glob
{
    /// <summary>Match <paramref name="pathPosix"/> (forward slashes, relative) against a find-tool glob.</summary>
    public static bool MatchFind(string pathPosix, string pattern)
    {
        pathPosix = pathPosix.Replace('\\', '/').TrimStart('/');
        if (pattern.Contains('/'))
        {
            var effective = pattern;
            if (!effective.StartsWith('/') && !effective.StartsWith("**/", StringComparison.Ordinal) && effective != "**")
            {
                effective = "**/" + effective;
            }
            if (effective.StartsWith('/')) effective = effective[1..];
            return IsMatch(pathPosix, effective);
        }

        var slash = pathPosix.LastIndexOf('/');
        var basename = slash < 0 ? pathPosix : pathPosix[(slash + 1)..];
        return IsMatch(basename, pattern);
    }

    /// <summary>Match a relative posix path against a grep <c>--glob</c> / gitignore-style pattern.</summary>
    public static bool IsMatch(string pathPosix, string pattern)
    {
        if (pattern.Length == 0) return false;
        var regex = ToRegex(pattern);
        return regex.IsMatch(pathPosix.Replace('\\', '/'));
    }

    /// <summary>Compile a gitignore-style glob to a regex matching a relative posix path.</summary>
    public static Regex ToRegex(string pattern, RegexOptions extra = RegexOptions.None)
    {
        var directoryOnly = pattern.EndsWith('/');
        if (directoryOnly) pattern = pattern[..^1];

        var anchored = pattern.StartsWith('/') || (pattern.Contains('/') && !pattern.EndsWith('/'));
        if (pattern.StartsWith('/')) pattern = pattern[1..];

        var body = GlobToRegexBody(pattern);
        string full;
        if (anchored)
        {
            full = "^" + body + (directoryOnly ? "(?:/|$)" : "$");
        }
        else
        {
            // Unanchored: match in any directory, including the root.
            full = "(?:^|.*/)" + body + (directoryOnly ? "(?:/|$)" : "$");
        }

        return new Regex(full, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking | extra);
    }

    private static string GlobToRegexBody(string pattern)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c == '*' && i + 1 < pattern.Length && pattern[i + 1] == '*')
            {
                i++;
                if (i + 1 < pattern.Length && pattern[i + 1] == '/')
                {
                    sb.Append("(?:.*/)?");
                    i++;
                }
                else
                {
                    sb.Append(".*");
                }
                continue;
            }

            switch (c)
            {
                case '*':
                    sb.Append("[^/]*");
                    break;
                case '?':
                    sb.Append("[^/]");
                    break;
                case '\\' when i + 1 < pattern.Length:
                    i++;
                    sb.Append(Regex.Escape(pattern[i].ToString()));
                    break;
                case '[':
                    var close = pattern.IndexOf(']', i + 1);
                    if (close < 0)
                    {
                        sb.Append("\\[");
                        break;
                    }
                    sb.Append(pattern, i, close - i + 1);
                    i = close;
                    break;
                default:
                    sb.Append(Regex.Escape(c.ToString()));
                    break;
            }
        }
        return sb.ToString();
    }
}
