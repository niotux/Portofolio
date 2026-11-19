namespace PiSharp.Core.Agent;

/// <summary>
/// Tools that only read, so several calls in one assistant message may run at once. The loop still
/// returns their results in call order. MCP tools are matched by suffix, since their names carry the
/// server alias (<c>kb__kb_get</c>). <c>chat_read</c> is not here: it moves the reader's cursor and marks
/// what it returns as read, so two of them in one message must keep their order.
/// </summary>
public static class ParallelSafeTools
{
    private static readonly string[] ReadOnlyMcpSuffixes =
        ["kb_get", "kb_search", "kb_list", "chat_search", "web_search", "web_fetch",
         "code_definition", "code_references", "code_hover", "code_symbols", "code_diagnostics"];

    public static bool IsParallelSafe(string name) =>
        name is "read" or "grep" or "find" or "ls"
        || ReadOnlyMcpSuffixes.Any(s => name.EndsWith(s, StringComparison.Ordinal));
}
