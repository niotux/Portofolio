// Ported from pi: packages/coding-agent/src/core/tools/grep.ts @ 71dca871b
// Managed walker instead of ripgrep (decision 5). Output format and notices match pi.
// Beyond pi: outputMode (content | files | count), offset paging, rg-style type filters and multiline
// regexes. ripgrep is still not used as a backend: its regex dialect and ignore rules differ from the
// managed walker's, so results would change with what happens to be installed on a host.
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PiSharp.Core.Agent;

namespace PiSharp.Tools;

/// <summary>Search file contents for a regex or literal pattern, respecting .gitignore.</summary>
public sealed class GrepTool : BuiltinTool<GrepArgs>
{
    public const int DefaultLimit = 100;

    /// <summary>rg <c>--type</c> names this tool accepts, with the extensions each selects.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> FileTypes = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["cs"] = [".cs", ".csx"],
        ["js"] = [".js", ".mjs", ".cjs", ".jsx"],
        ["ts"] = [".ts", ".tsx", ".mts", ".cts"],
        ["py"] = [".py", ".pyi"],
        ["json"] = [".json"],
        ["md"] = [".md", ".markdown"],
        ["yaml"] = [".yaml", ".yml"],
        ["sh"] = [".sh", ".bash"],
        ["go"] = [".go"],
        ["rs"] = [".rs"],
        ["java"] = [".java"],
        ["sql"] = [".sql"],
        ["html"] = [".html", ".htm"],
        ["css"] = [".css"],
    };

    private static readonly JsonObject ArgsSchema = Schema.Parse("""
        {
          "type": "object",
          "properties": {
            "pattern": { "type": "string", "description": "Search pattern (regex or literal string)" },
            "path": { "type": "string", "description": "Directory or file to search (default: current directory)" },
            "glob": { "type": "string", "description": "Filter files by glob pattern, e.g. '*.ts' or '**/*.spec.ts'" },
            "type": { "type": "string", "description": "Filter files by type like rg --type: cs, js, ts, py, json, md, yaml, sh, go, rs, java, sql, html, css" },
            "ignoreCase": { "type": "boolean", "description": "Case-insensitive search (default: false)" },
            "literal": { "type": "boolean", "description": "Treat pattern as literal string instead of regex (default: false)" },
            "multiline": { "type": "boolean", "description": "Match the pattern against whole files so it can span lines, e.g. 'class Foo[\\s\\S]*?Bar' (default: false)" },
            "outputMode": { "type": "string", "enum": ["content", "files", "count"], "description": "content: matching lines (default); files: unique file paths only; count: match count per file" },
            "context": { "type": "number", "description": "Number of lines to show before and after each match (default: 0; content mode only)" },
            "limit": { "type": "number", "description": "Maximum number of results (matches, files or counts) to return (default: 100)" },
            "offset": { "type": "number", "description": "Skip this many results first, to page through a long result (default: 0)" }
          },
          "required": ["pattern"]
        }
        """);

    private static readonly string Describe =
        $"Search file contents for a pattern. Returns matching lines with file paths and line numbers; outputMode=files lists matching files and outputMode=count per-file counts. Respects .gitignore. " +
        $"Output is truncated to {DefaultLimit} results or {Truncation.DefaultMaxBytes / 1024}KB (whichever is hit first); continue with the offset it names. " +
        $"Long lines are truncated to {Truncation.GrepMaxLineLength} chars.";

    public GrepTool(string cwd)
        : base("grep", "grep", Describe, ArgsSchema, ToolsJsonContext.Default.GrepArgs, cwd)
    {
    }

    /// <summary>One match: 1-based first and last line it spans, and the lines of its file.</summary>
    private readonly record struct Hit(string FilePath, int StartLine, int EndLine, string[] Lines);

    protected override Task<AgentToolResult> ExecuteAsync(
        string toolCallId, GrepArgs args, Action<AgentToolResult>? onUpdate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var searchPath = Resolve(args.Path ?? ".");
        var contextValue = args.Context is > 0 ? args.Context.Value : 0;
        var effectiveLimit = Math.Max(1, args.Limit ?? DefaultLimit);
        var offset = Math.Max(0, args.Offset ?? 0);
        var mode = string.IsNullOrEmpty(args.OutputMode) ? "content" : args.OutputMode.ToLowerInvariant();
        if (mode is not ("content" or "files" or "count"))
            throw new InvalidOperationException($"Unknown outputMode \"{args.OutputMode}\". Use content, files or count.");
        string[]? extensions = null;
        if (args.Type is { Length: > 0 } type && !FileTypes.TryGetValue(type.TrimStart('.'), out extensions))
            throw new InvalidOperationException($"Unknown file type \"{type}\". Known types: {string.Join(", ", FileTypes.Keys)}.");
        var multiline = args.Multiline == true;

        bool isDirectory;
        if (Directory.Exists(searchPath)) isDirectory = true;
        else if (File.Exists(searchPath)) isDirectory = false;
        else throw new InvalidOperationException($"Path not found: {searchPath}");

        Regex regex;
        try
        {
            var options = RegexOptions.CultureInvariant;
            if (args.IgnoreCase == true) options |= RegexOptions.IgnoreCase;
            // Multiline: ^ and $ still anchor at line boundaries, as in rg -U; '.' does not cross a newline.
            if (multiline) options |= RegexOptions.Multiline;
            var body = args.Literal == true ? Regex.Escape(args.Pattern) : args.Pattern;
            regex = new Regex(body, options, TimeSpan.FromSeconds(5));
        }
        catch (ArgumentException e)
        {
            throw new InvalidOperationException($"Invalid regular expression: {e.Message}");
        }

        // Results in order; the first `offset` are skipped, and one past the limit only proves there are more.
        var hits = new List<Hit>();
        var files = new List<(string FilePath, int Count)>();
        var skipped = 0;
        var moreResults = false;
        bool Take(Action add, int taken)
        {
            if (skipped < offset) { skipped++; return true; }
            if (taken < effectiveLimit) { add(); return true; }
            moreResults = true;
            return false;
        }

        IEnumerable<string> candidates = isDirectory
            ? GitIgnore.WalkFiles(searchPath, cancellationToken)
            : [searchPath];

        foreach (var filePath in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (isDirectory && args.Glob is { Length: > 0 })
            {
                var relative = ToolPaths.RelativePosix(searchPath, filePath);
                if (!Glob.IsMatch(relative, args.Glob)) continue;
            }
            if (isDirectory && extensions is not null
                && !extensions.Contains(Path.GetExtension(filePath), StringComparer.OrdinalIgnoreCase)) continue;

            if (IsBinary(filePath)) continue;

            string text;
            try
            {
                text = File.ReadAllText(filePath);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            var lines = text.Split('\n');
            var spans = multiline ? MultilineSpans(regex, text) : LineSpans(regex, lines);

            var keepGoing = true;
            if (mode == "content")
            {
                foreach (var (start, end) in spans)
                {
                    if (!Take(() => hits.Add(new Hit(filePath, start, end, lines)), hits.Count)) { keepGoing = false; break; }
                }
            }
            else if (mode == "files")
            {
                if (spans.Any()) keepGoing = Take(() => files.Add((filePath, 1)), files.Count);
            }
            else
            {
                var count = spans.Count();
                if (count > 0) keepGoing = Take(() => files.Add((filePath, count)), files.Count);
            }
            if (!keepGoing) break;
        }

        if (hits.Count == 0 && files.Count == 0)
        {
            return Task.FromResult(AgentToolResult.FromText(skipped > 0 ? $"No more matches (offset={offset} skipped all {skipped} results)" : "No matches found"));
        }

        var outputLines = new List<string>();
        var linesTruncated = false;

        if (mode == "content")
        {
            foreach (var hit in hits)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relativePath = FormatPath(hit.FilePath, searchPath, isDirectory);
                var start = Math.Max(1, hit.StartLine - contextValue);
                var end = Math.Min(hit.Lines.Length, hit.EndLine + contextValue);
                for (var current = start; current <= end; current++)
                {
                    var lineText = hit.Lines[current - 1].Replace("\r", "");
                    var (truncatedText, wasTruncated) = Truncation.TruncateLine(lineText);
                    if (wasTruncated) linesTruncated = true;
                    outputLines.Add(current >= hit.StartLine && current <= hit.EndLine
                        ? $"{relativePath}:{current}: {truncatedText}"
                        : $"{relativePath}-{current}- {truncatedText}");
                }
            }
        }
        else
        {
            foreach (var (filePath, count) in files)
            {
                var relativePath = FormatPath(filePath, searchPath, isDirectory);
                outputLines.Add(mode == "count" ? $"{relativePath}:{count}" : relativePath);
            }
        }

        var shown = mode == "content" ? hits.Count : files.Count;
        var truncation = Truncation.TruncateHead(string.Join('\n', outputLines), Truncation.NoLineLimit);
        var output = truncation.Content;
        var details = new JsonObject();
        var notices = new List<string>();
        if (moreResults)
        {
            var unit = mode == "content" ? "matches" : mode == "files" ? "files" : "counts";
            notices.Add($"{effectiveLimit} {unit} limit reached. Use limit={effectiveLimit * 2} for more, or refine pattern");
            notices.Add($"more results: offset={offset + shown}");
            details["matchLimitReached"] = effectiveLimit;
            details["nextOffset"] = offset + shown;
        }
        if (truncation.Truncated)
        {
            notices.Add($"{Truncation.FormatSize(Truncation.DefaultMaxBytes)} limit reached");
            details["truncation"] = truncation.ToJson();
        }
        if (linesTruncated)
        {
            notices.Add($"Some lines truncated to {Truncation.GrepMaxLineLength} chars. Use read tool to see full lines");
            details["linesTruncated"] = true;
        }
        if (notices.Count > 0) output += $"\n\n[{string.Join(". ", notices)}]";

        return Task.FromResult(new AgentToolResult([new Core.Messages.TextContent(output)], details.Count > 0 ? details : null));
    }

    /// <summary>Line-by-line matching: every matching line is one result spanning that line.</summary>
    private static IEnumerable<(int Start, int End)> LineSpans(Regex regex, string[] lines)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            if (regex.IsMatch(lines[i])) yield return (i + 1, i + 1);
        }
    }

    /// <summary>Whole-text matching: every regex match is one result spanning the lines it touches.</summary>
    private static IEnumerable<(int Start, int End)> MultilineSpans(Regex regex, string text)
    {
        var lineStarts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n') lineStarts.Add(i + 1);
        }
        int LineOf(int index)
        {
            var at = lineStarts.BinarySearch(index);
            return (at >= 0 ? at : ~at - 1) + 1;
        }
        for (var match = regex.Match(text); match.Success; match = match.NextMatch())
        {
            var start = LineOf(match.Index);
            // A match that ends with its newline does not reach into the next line.
            var end = match.Length == 0 ? start : LineOf(match.Index + match.Length - 1);
            yield return (start, end);
        }
    }

    private static string FormatPath(string filePath, string searchPath, bool isDirectory)
    {
        if (isDirectory)
        {
            var relative = ToolPaths.RelativePosix(searchPath, filePath);
            if (relative.Length > 0 && !relative.StartsWith("..", StringComparison.Ordinal)) return relative;
        }
        return Path.GetFileName(filePath);
    }

    private static bool IsBinary(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buffer = new byte[8192];
            var n = stream.Read(buffer, 0, buffer.Length);
            return buffer.AsSpan(0, n).IndexOf((byte)0) >= 0;
        }
        catch
        {
            return true;
        }
    }
}
