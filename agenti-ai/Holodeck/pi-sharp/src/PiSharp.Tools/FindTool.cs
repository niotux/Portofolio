// Ported from pi: packages/coding-agent/src/core/tools/find.ts @ 71dca871b
// Managed walker instead of fd (decision 5). Output format and notices match pi.
using System.Text.Json.Nodes;
using PiSharp.Core.Agent;

namespace PiSharp.Tools;

/// <summary>Find files by glob pattern, respecting .gitignore. fd basename-vs-full-path semantics.</summary>
public sealed class FindTool : BuiltinTool<FindArgs>
{
    public const int DefaultLimit = 1000;

    private static readonly JsonObject ArgsSchema = Schema.Parse("""
        {
          "type": "object",
          "properties": {
            "pattern": { "type": "string", "description": "Glob pattern to match files, e.g. '*.ts', '**/*.json', or 'src/**/*.spec.ts'" },
            "path": { "type": "string", "description": "Directory to search in (default: current directory)" },
            "limit": { "type": "number", "description": "Maximum number of results (default: 1000)" }
          },
          "required": ["pattern"]
        }
        """);

    private static readonly string Describe =
        $"Search for files by glob pattern. Returns matching file paths relative to the search directory. Respects .gitignore. " +
        $"Output is truncated to {DefaultLimit} results or {Truncation.DefaultMaxBytes / 1024}KB (whichever is hit first).";

    public FindTool(string cwd)
        : base("find", "find", Describe, ArgsSchema, ToolsJsonContext.Default.FindArgs, cwd)
    {
    }

    /// <summary>Relativize a find result against the search root and normalize to posix separators.</summary>
    public static string RelativizeFindResultPath(string resultPath, string searchPath)
    {
        var hadTrailingSeparator = resultPath.EndsWith(Path.DirectorySeparatorChar) || resultPath.EndsWith('/');
        var relativePath = Path.IsPathRooted(resultPath)
            ? Path.GetRelativePath(searchPath, resultPath)
            : resultPath;
        var posixPath = relativePath.Replace('\\', '/');
        return hadTrailingSeparator && !posixPath.EndsWith('/') ? posixPath + "/" : posixPath;
    }

    protected override Task<AgentToolResult> ExecuteAsync(
        string toolCallId, FindArgs args, Action<AgentToolResult>? onUpdate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var searchPath = Resolve(args.Path ?? ".");
        var effectiveLimit = args.Limit ?? DefaultLimit;
        if (!Directory.Exists(searchPath) && !File.Exists(searchPath))
        {
            throw new InvalidOperationException($"Path not found: {searchPath}");
        }

        var hits = new List<string>();
        var resultLimitReached = false;

        if (File.Exists(searchPath) && !Directory.Exists(searchPath))
        {
            var name = Path.GetFileName(searchPath);
            if (Glob.MatchFind(name, args.Pattern)) hits.Add(name);
        }
        else
        {
            foreach (var (path, _) in GitIgnore.Walk(searchPath, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = RelativizeFindResultPath(path, searchPath);
                if (!Glob.MatchFind(relative, args.Pattern)) continue;
                hits.Add(relative);
                if (hits.Count >= effectiveLimit)
                {
                    resultLimitReached = true;
                    break;
                }
            }
        }

        if (hits.Count == 0) return Task.FromResult(AgentToolResult.FromText("No files found matching pattern"));

        var truncation = Truncation.TruncateHead(string.Join('\n', hits), Truncation.NoLineLimit);
        var resultOutput = truncation.Content;
        var details = new JsonObject();
        var notices = new List<string>();
        if (resultLimitReached)
        {
            notices.Add($"{effectiveLimit} results limit reached. Use limit={effectiveLimit * 2} for more, or refine pattern");
            details["resultLimitReached"] = effectiveLimit;
        }
        if (truncation.Truncated)
        {
            notices.Add($"{Truncation.FormatSize(Truncation.DefaultMaxBytes)} limit reached");
            details["truncation"] = truncation.ToJson();
        }
        if (notices.Count > 0) resultOutput += $"\n\n[{string.Join(". ", notices)}]";

        return Task.FromResult(new AgentToolResult(
            [new Core.Messages.TextContent(resultOutput)],
            details.Count > 0 ? details : null));
    }
}
