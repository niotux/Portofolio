using System.Text.RegularExpressions;

namespace PiSharp.Core.Verification;

/// <summary>Parses the <c>check</c> field of a plan-step into a <see cref="DoneCheck"/> that the harness can run.
/// Two forms are supported:
/// <list type="bullet">
/// <item><c>file:path/to/file</c> → a FileExists check on the path (relative to the working directory).</item>
/// <item><c>grep:pattern:path</c> → a Grep check: the pattern must appear in the file or directory.</item>
/// </list>
/// Anything else (no recognised prefix) is treated as a DoneCheck id to look up from the task's declared
/// checks (spec fence or .pi/done-checks.json). Returns null when the string is empty or whitespace.</summary>
public static class PlanCheck
{
    /// <summary>Parses one plan-step check string. Returns null for empty/whitespace input.</summary>
    public static DoneCheck? Parse(string? check, string task)
    {
        if (string.IsNullOrWhiteSpace(check)) return null;
        var spec = check.Trim();
        if (spec.StartsWith("file:", StringComparison.Ordinal))
        {
            var path = spec[5..].Trim();
            if (path.Length == 0) return null;
            return new DoneCheck($"plan-{task}-file", DoneCheckKind.FileExists, path);
        }
        if (spec.StartsWith("grep:", StringComparison.Ordinal))
        {
            // grep:pattern::path — the double colon matches the Verifier Grep spec ("pattern::target");
            // a single-colon or space separator would be ambiguous because patterns can contain colons.
            var rest = spec[5..];
            var sep = rest.IndexOf("::", StringComparison.Ordinal);
            if (sep < 1) return null;
            var pattern = rest[..sep].Trim();
            var path = rest[(sep + 2)..].Trim();
            if (pattern.Length == 0 || path.Length == 0) return null;
            return new DoneCheck($"plan-{task}-grep", DoneCheckKind.Grep, $"{pattern}::{path}");
        }
        // No recognised prefix: treat as a DoneCheck id to look up from the task's declared checks.
        // The caller (PlanCheckRunner) resolves it.
        return new DoneCheck(spec, DoneCheckKind.Command, spec);
    }

    /// <summary>True when the check string is a resolvable DoneCheck id (no inline prefix) and therefore
    /// needs to be looked up from the task's declared checks.</summary>
    public static bool IsIdReference(string? check)
    {
        if (string.IsNullOrWhiteSpace(check)) return false;
        var spec = check.Trim();
        return !spec.StartsWith("file:", StringComparison.Ordinal) && !spec.StartsWith("grep:", StringComparison.Ordinal);
    }
}
