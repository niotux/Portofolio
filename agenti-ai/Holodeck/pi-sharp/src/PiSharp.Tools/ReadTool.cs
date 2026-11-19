// Ported from pi: packages/coding-agent/src/core/tools/read.ts @ 71dca871b
using System.Text;
using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Messages;
using PiSharp.Core.Skills;

namespace PiSharp.Tools;

/// <summary>Read a text file with offset/limit continuation, or attach an image.</summary>
public sealed class ReadTool : BuiltinTool<ReadArgs>
{
    private static readonly JsonObject ArgsSchema = Schema.Parse("""
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "Path to the file to read (relative or absolute)" },
            "offset": { "type": "number", "description": "Line number to start reading from (1-indexed)" },
            "limit": { "type": "number", "description": "Maximum number of lines to read" },
            "refresh": { "type": "boolean", "description": "Return full contents even if an identical version was already read in this context" }
          },
          "required": ["path"]
        }
        """);

    private static readonly string Describe =
        $"Read the contents of a file. Supports text files and images (jpg, png, gif, webp, bmp). " +
        $"Images are sent as attachments. For text files, output is truncated to {Truncation.DefaultMaxLines} lines " +
        $"or {Truncation.DefaultMaxBytes / 1024}KB (whichever is hit first). Use offset/limit for large files. " +
        $"When you need the full file, continue with offset until complete.";

    public ReadTool(string cwd, bool supportsImages = true)
        : base("read", "read", Describe, ArgsSchema, ToolsJsonContext.Default.ReadArgs, cwd)
    {
        SupportsImages = supportsImages;
    }

    /// <summary>When false, an image is described but not attached (pi's non-vision note).</summary>
    public bool SupportsImages { get; set; }

    /// <summary>Pixel threshold for the model-facing image warning; 0 disables it.</summary>
    public int WarningPixels { get; set; } = ImageWarnings.DefaultWarnPixels;

    /// <summary>Model context window used for the 25% estimated-token warning.</summary>
    public int ContextWindow { get; set; }

    /// <summary>Receives the same image warning that is added to the tool result.</summary>
    public Action<string>? WarningSink { get; set; }

    /// <summary>Resolves <c>skill://</c> URIs via MCP <c>resources/read</c>. Null = those URIs fail.</summary>
    public ISkillUriResolver? SkillUriResolver { get; set; }

    public const string NonVisionNote = "[Current model does not support images. The image will be omitted from this request.]";

    public override async ValueTask<string?> ReadCacheKeyAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        if (arguments["refresh"]?.GetValue<bool>() == true) return null;
        var path = arguments["path"]?.GetValue<string>();
        if (path is null || path.StartsWith("skill://", StringComparison.OrdinalIgnoreCase)) return null;
        var absolute = Resolve(path);
        if (await ImageSniffer.DetectFromFileAsync(absolute, cancellationToken).ConfigureAwait(false) is not null) return null;
        var bytes = await File.ReadAllBytesAsync(absolute, cancellationToken).ConfigureAwait(false);
        return absolute + ":" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)) + ":" + ToolCatalog.Canonical(arguments);
    }

    public override async ValueTask<bool> AcceptCachedReadAsync(JsonObject arguments, string key, CancellationToken cancellationToken)
    {
        var absolute = Resolve(arguments["path"]!.GetValue<string>());
        var bytes = await File.ReadAllBytesAsync(absolute, cancellationToken).ConfigureAwait(false);
        var actual = absolute + ":" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)) + ":" + ToolCatalog.Canonical(arguments);
        if (actual != key) return false;
        ReadTracker?.Record(absolute, bytes);
        return true;
    }

    protected override async Task<AgentToolResult> ExecuteAsync(
        string toolCallId, ReadArgs args, Action<AgentToolResult>? onUpdate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (args.Path.StartsWith("skill://", StringComparison.OrdinalIgnoreCase))
        {
            if (SkillUriResolver is null)
                throw new InvalidOperationException($"Cannot read {args.Path}: no skill URI resolver is configured.");
            var text = await SkillUriResolver.ReadTextAsync(args.Path, cancellationToken).ConfigureAwait(false);
            return ReadText(Encoding.UTF8.GetBytes(text), args);
        }

        var absolutePath = Resolve(args.Path);
        if (!File.Exists(absolutePath)) throw new FileNotFoundException($"File not found: {absolutePath}", absolutePath);

        var mimeType = await ImageSniffer.DetectFromFileAsync(absolutePath, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var bytes = await File.ReadAllBytesAsync(absolutePath, cancellationToken).ConfigureAwait(false);
        if (mimeType is not null || (bytes.Length == 0 && ImageValidation.IsImageFileName(absolutePath)))
            ImageValidation.RequireValid(bytes, mimeType ?? "image/png", absolutePath);
        var result = mimeType is not null ? ReadImage(bytes, mimeType) : ReadText(bytes, args);
        // Recorded only once the read succeeded; a partial read counts, the hash is of the whole file.
        ReadTracker?.Record(absolutePath, bytes);
        return result;
    }

    private AgentToolResult ReadImage(byte[] bytes, string mimeType)
    {
        var note = $"Read image file [{mimeType}]";
        // adr-pi-sharp-image-size-warning: the warning tells the model to crop or downscale the
        // image. For a model that cannot take images (SupportsImages=false) that advice is moot,
        // so it is skipped; the image is still attached exactly as before.
        var warning = SupportsImages ? ImageWarnings.For(bytes, WarningPixels, ContextWindow) : null;
        if (warning is not null)
        {
            note += "\n" + warning;
            WarningSink?.Invoke(warning);
        }
        if (!SupportsImages) note += "\n" + NonVisionNote;

        return new AgentToolResult([
            new TextContent(note),
            new ImageContent(Convert.ToBase64String(bytes), mimeType),
        ]);
    }

    private static AgentToolResult ReadText(byte[] bytes, ReadArgs args)
    {
        var text = Encoding.UTF8.GetString(bytes);
        var allLines = text.Split('\n');
        var totalFileLines = allLines.Length;

        // The offset argument is 1-indexed; the array is 0-indexed.
        var startLine = args.Offset is > 0 ? args.Offset.Value - 1 : 0;
        var startLineDisplay = startLine + 1;
        if (startLine >= allLines.Length)
        {
            throw new InvalidOperationException($"Offset {args.Offset} is beyond end of file ({allLines.Length} lines total)");
        }

        // A user limit is honored first; truncateHead then applies the global limits.
        string selected;
        int? userLimitedLines = null;
        if (args.Limit is not null)
        {
            var endLine = Math.Min(startLine + Math.Max(0, args.Limit.Value), allLines.Length);
            selected = string.Join("\n", allLines[startLine..endLine]);
            userLimitedLines = endLine - startLine;
        }
        else
        {
            selected = string.Join("\n", allLines[startLine..]);
        }

        var truncation = Truncation.TruncateHead(selected);
        string outputText;
        JsonNode? details = null;

        if (truncation.FirstLineExceedsLimit)
        {
            // Nothing can be shown; point the model at a bash fallback for that one line.
            var firstLineSize = Truncation.FormatSize(Truncation.ByteLength(allLines[startLine]));
            outputText =
                $"[Line {startLineDisplay} is {firstLineSize}, exceeds {Truncation.FormatSize(Truncation.DefaultMaxBytes)} limit. " +
                $"Use bash: sed -n '{startLineDisplay}p' {args.Path} | head -c {Truncation.DefaultMaxBytes}]";
            details = Details(truncation);
        }
        else if (truncation.Truncated)
        {
            var endLineDisplay = startLineDisplay + truncation.OutputLines - 1;
            var nextOffset = endLineDisplay + 1;
            outputText = truncation.Content + (truncation.TruncatedBy == TruncatedBy.Lines
                ? $"\n\n[Showing lines {startLineDisplay}-{endLineDisplay} of {totalFileLines}. Use offset={nextOffset} to continue.]"
                : $"\n\n[Showing lines {startLineDisplay}-{endLineDisplay} of {totalFileLines} ({Truncation.FormatSize(Truncation.DefaultMaxBytes)} limit). Use offset={nextOffset} to continue.]");
            details = Details(truncation);
        }
        else if (userLimitedLines is not null && startLine + userLimitedLines < allLines.Length)
        {
            // The user's own limit stopped early while the file still has more lines.
            var remaining = allLines.Length - (startLine + userLimitedLines.Value);
            var nextOffset = startLine + userLimitedLines.Value + 1;
            outputText = $"{truncation.Content}\n\n[{remaining} more lines in file. Use offset={nextOffset} to continue.]";
        }
        else
        {
            outputText = truncation.Content;
        }

        return new AgentToolResult([new TextContent(outputText)], details);
    }

    private static JsonObject Details(TruncationResult truncation) => new() { ["truncation"] = truncation.ToJson() };
}
