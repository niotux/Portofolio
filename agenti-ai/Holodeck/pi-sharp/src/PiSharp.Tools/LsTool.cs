// Ported from pi: packages/coding-agent/src/core/tools/ls.ts @ 71dca871b
using System.Text.Json.Nodes;
using PiSharp.Core.Agent;

namespace PiSharp.Tools;

/// <summary>List a directory: entries sorted case-insensitively, directories marked with a slash.</summary>
public sealed class LsTool : BuiltinTool<LsArgs>
{
    public const int DefaultLimit = 500;

    private static readonly JsonObject ArgsSchema = Schema.Parse("""
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "Directory to list (default: current directory)" },
            "limit": { "type": "number", "description": "Maximum number of entries to return (default: 500)" }
          }
        }
        """);

    private static readonly string Describe =
        $"List directory contents. Returns entries sorted alphabetically, with '/' suffix for directories. " +
        $"Includes dotfiles. Output is truncated to {DefaultLimit} entries or {Truncation.DefaultMaxBytes / 1024}KB " +
        $"(whichever is hit first).";

    public LsTool(string cwd)
        : base("ls", "ls", Describe, ArgsSchema, ToolsJsonContext.Default.LsArgs, cwd)
    {
    }

    protected override Task<AgentToolResult> ExecuteAsync(
        string toolCallId, LsArgs args, Action<AgentToolResult>? onUpdate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var dirPath = Resolve(args.Path ?? ".");
        var limit = args.Limit ?? DefaultLimit;

        if (!Directory.Exists(dirPath))
        {
            if (File.Exists(dirPath)) throw new InvalidOperationException($"Not a directory: {dirPath}");
            throw new DirectoryNotFoundException($"Path not found: {dirPath}");
        }

        string[] entries;
        try
        {
            entries = Directory.GetFileSystemEntries(dirPath).Select(Path.GetFileName).ToArray()!;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Cannot read directory: {e.Message}");
        }

        Array.Sort(entries, static (a, b) => string.Compare(a, b, StringComparison.OrdinalIgnoreCase));

        var results = new List<string>();
        var entryLimitReached = false;
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (results.Count >= limit)
            {
                entryLimitReached = true;
                break;
            }

            var fullPath = Path.Combine(dirPath, entry);
            if (Directory.Exists(fullPath)) results.Add(entry + "/");
            else if (File.Exists(fullPath)) results.Add(entry);
            // Anything that cannot be stat'ed (a broken symlink, for instance) is skipped, like in pi.
        }

        if (results.Count == 0) return Task.FromResult(AgentToolResult.FromText("(empty directory)"));

        // Only the byte limit applies here: the entry count is already capped by `limit`.
        var truncation = Truncation.TruncateHead(string.Join("\n", results), Truncation.NoLineLimit);
        var output = truncation.Content;
        var details = new JsonObject();
        var notices = new List<string>();

        if (entryLimitReached)
        {
            notices.Add($"{limit} entries limit reached. Use limit={limit * 2} for more");
            details["entryLimitReached"] = limit;
        }
        if (truncation.Truncated)
        {
            notices.Add($"{Truncation.FormatSize(Truncation.DefaultMaxBytes)} limit reached");
            details["truncation"] = truncation.ToJson();
        }
        if (notices.Count > 0) output += $"\n\n[{string.Join(". ", notices)}]";

        return Task.FromResult(new AgentToolResult([new Core.Messages.TextContent(output)], details.Count > 0 ? details : null));
    }
}
