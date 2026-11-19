namespace PiSharp.Core.Agent;

/// <summary>
/// A patch typed into the shell (Codex intercepts the same: <c>apply_patch &lt;&lt;'EOF' ... EOF</c>). GPT models are trained
/// to write it that way; without this the shell answers "apply_patch: command not found" and the turn is lost. The body goes
/// to the apply_patch tool instead, with its read-before-write check and the guard (plan-pi-sharp-codex item 7).
/// </summary>
public static class ShellPatch
{
    /// <summary>The patch body when <paramref name="command"/> is only an apply_patch heredoc holding a patch; else null.
    /// A plain scan, not a regex: a 250 ms regex timeout on a loaded seat (load 24, 2026-10-26) said "not a patch", and the
    /// shell then answered "apply_patch: command not found".</summary>
    public static string? Extract(string command)
    {
        var text = command.Replace("\r\n", "\n").TrimStart();
        if (!text.StartsWith("apply_patch", StringComparison.Ordinal)) return null;
        var at = "apply_patch".Length;
        while (at < text.Length && text[at] is ' ' or '\t') at++;
        if (!text.AsSpan(at).StartsWith("<<", StringComparison.Ordinal)) return null;
        at += 2;
        if (at < text.Length && text[at] == '-') at++;
        while (at < text.Length && text[at] is ' ' or '\t') at++;
        var quote = at < text.Length && text[at] is '\'' or '"' ? text[at++] : '\0';
        var tagStart = at;
        if (at >= text.Length || !(char.IsAsciiLetter(text[at]) || text[at] == '_')) return null;
        while (at < text.Length && (char.IsAsciiLetterOrDigit(text[at]) || text[at] == '_')) at++;
        var tag = text[tagStart..at];
        if (quote != '\0') { if (at >= text.Length || text[at] != quote) return null; at++; }
        while (at < text.Length && text[at] is ' ' or '\t') at++;
        if (at >= text.Length || text[at] != '\n') return null;
        var lines = text[(at + 1)..].TrimEnd().Split('\n');
        if (lines.Length < 2 || lines[^1].Trim(' ', '\t') != tag) return null;
        var body = string.Join('\n', lines[..^1]);
        return body.TrimStart().StartsWith("*** Begin Patch", StringComparison.Ordinal) ? body : null;
    }
}
