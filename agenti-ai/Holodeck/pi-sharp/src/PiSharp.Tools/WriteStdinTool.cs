// plan-pi-sharp-codex item 6 (Codex unified exec): type into a running command and read what it printed since. A bash command
// still running at its yield becomes a session (a job); with tty=true its input stays open, so an interactive program (a
// prompt, a REPL, a TUI asking a question) can be driven turn by turn. Empty chars only polls.
using System.Text;
using System.Text.Json.Nodes;
using PiSharp.Core.Agent;

namespace PiSharp.Tools;

public sealed class WriteStdinTool(JobTool jobs) : IAgentTool
{
    public const string ToolName = "write_stdin";
    public const int DefaultYieldMs = 1_000;
    public const int MaxYieldMs = 30_000;
    public const int DefaultMaxOutputTokens = 10_000;

    private static readonly JsonObject ArgsSchema = JsonNode.Parse("""
        {
          "type": "object",
          "properties": {
            "session_id": { "type": "string", "description": "The job id a yielded bash command returned (job-...)" },
            "chars": { "type": "string", "description": "What to type, exactly: add \n for Enter, \u0003 for Ctrl-C, \u0004 for Ctrl-D. Empty: only read the new output" },
            "yield_time_ms": { "type": "number", "description": "How long to wait for output after typing (default 1000, max 30000); returns sooner if the command exits" },
            "max_output_tokens": { "type": "number", "description": "Cap on the output returned (default 10000 tokens); unread output remains for the next poll; more_output says when another chunk is available" }
          },
          "required": ["session_id"]
        }
        """)!.AsObject();

    public string Name => ToolName;
    public string Label => ToolName;
    public string Description =>
        "Type into a command that is still running and read what it printed since (Codex write_stdin). A bash command still " +
        "running at its yield becomes a session whose id comes back as job_id; run it with tty=true when it needs a terminal " +
        "and input (an interactive prompt, a REPL). Empty chars only reads. The result says whether it still runs and its exit code.";
    public JsonObject Parameters => ArgsSchema;
    public ToolExecutionMode? ExecutionMode => ToolExecutionMode.Sequential;
    public TimeSpan? MaxDuration => TimeSpan.FromSeconds(MaxYieldMs / 1000 + 30);

    /// <summary>A number argument, whatever JSON number type it holds (an int built in code, a double parsed); null when absent.</summary>
    internal static double? Number(JsonNode? node) =>
        node is JsonValue && double.TryParse(node.ToJsonString().Trim('"'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : null;

    public Task<AgentToolResult> ExecuteAsync(string toolCallId, JsonObject arguments, Action<AgentToolResult>? onUpdate, CancellationToken cancellationToken)
    {
        var id = arguments["session_id"]?.GetValue<string>() ?? throw new ToolArgumentException("session_id required: the job id bash returned");
        var chars = arguments["chars"]?.GetValue<string>() ?? "";
        var yieldMs = Math.Clamp(Number(arguments["yield_time_ms"]) ?? DefaultYieldMs, 0, MaxYieldMs);
        var maxTokens = (int)Math.Clamp(Number(arguments["max_output_tokens"]) ?? DefaultMaxOutputTokens, 100, 200_000);
        return jobs.WriteStdinAsync(id, chars, TimeSpan.FromMilliseconds(yieldMs), maxTokens, cancellationToken);
    }
}

/// <summary>Output cut to a token budget the way Codex cuts it: the head and the tail kept, the middle replaced by how much was
/// left out, and the original size said first.</summary>
public static class OutputCap
{
    /// <summary>About four bytes of text to a token, the estimate Codex's truncation uses.</summary>
    public const int BytesPerToken = 4;

    public static string MiddleCut(string text, int maxTokens)
    {
        var maxBytes = Math.Max(1, maxTokens) * BytesPerToken;
        var bytes = Encoding.UTF8.GetByteCount(text);
        if (bytes <= maxBytes) return text;
        var keep = maxBytes / 2;
        var head = Prefix(text, keep);
        var tail = Suffix(text, keep);
        var omitted = Math.Max(0, bytes - Encoding.UTF8.GetByteCount(head) - Encoding.UTF8.GetByteCount(tail));
        var lines = text.Count(c => c == '\n') + (text.EndsWith('\n') ? 0 : 1);
        return $"Original output: about {(bytes + BytesPerToken - 1) / BytesPerToken} tokens, {lines} lines; cut to {maxTokens}.\n" +
            head + $"\n…{(omitted + BytesPerToken - 1) / BytesPerToken} tokens left out…\n" + tail;
    }

    private static string Prefix(string text, int maxBytes)
    {
        var n = Math.Min(text.Length, maxBytes);
        while (n > 0 && Encoding.UTF8.GetByteCount(text.AsSpan(0, n)) > maxBytes) n--;
        if (n > 0 && char.IsHighSurrogate(text[n - 1])) n--;
        return text[..n];
    }

    private static string Suffix(string text, int maxBytes)
    {
        var n = Math.Min(text.Length, maxBytes);
        while (n > 0 && Encoding.UTF8.GetByteCount(text.AsSpan(text.Length - n)) > maxBytes) n--;
        if (n > 0 && char.IsLowSurrogate(text[text.Length - n])) n--;
        return text[^n..];
    }
}
