// Ported from pi: packages/coding-agent/src/core/tools/edit.ts @ 71dca871b
using System.Text.Json;
using System.Text.Json.Nodes;
using PiSharp.Core.Agent;

namespace PiSharp.Tools;

/// <summary>Exact-text file editor with an opt-in fuzzy-match fallback and a display diff.</summary>
public sealed class EditTool : BuiltinTool<EditArgs>
{
    private static readonly JsonObject ArgsSchema = Schema.Parse("""
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "Path to the file to edit (relative or absolute)" },
            "edits": {
              "type": "array",
              "description": "One or more targeted replacements. Each edit is matched against the original file, not incrementally. Do not include overlapping or nested edits. If two changes touch the same block or nearby lines, merge them into one edit instead.",
              "items": {
                "type": "object",
                "properties": {
                  "oldText": {
                    "type": "string",
                    "description": "Exact text for one targeted replacement. It must be unique in the original file and must not overlap with any other edits[].oldText in the same call."
                  },
                  "newText": { "type": "string", "description": "Replacement text for this targeted edit." }
                },
                "required": ["oldText", "newText"]
              }
            },
            "fuzzy": {
              "type": "boolean",
              "description": "Also match oldText ignoring trailing whitespace, smart quotes, dash and unicode-space differences (default: false, exact match only)."
            }
          },
          "required": ["path", "edits"]
        }
        """);

    private const string Describe =
        "Edit a single file using exact text replacement. Read the file first: a file not read in this session, or changed since, is refused. Every edits[].oldText must match a unique, non-overlapping region of the original file. If two changes affect the same block or nearby lines, merge them into one edit instead of emitting overlapping edits. Do not include large unchanged regions just to connect distant changes.";

    public EditTool(string cwd)
        : base("edit", "edit", Describe, ArgsSchema, ToolsJsonContext.Default.EditArgs, cwd)
    {
    }

    /// <summary>
    /// Lift a JSON-string or single-object <c>edits</c> value, and the legacy
    /// <c>oldText</c>/<c>newText</c> pair, into an <c>edits</c> array.
    /// </summary>
    public override JsonNode? PrepareArguments(JsonNode? arguments)
    {
        if (arguments is not JsonObject obj) return arguments;

        if (obj["edits"] is JsonValue editsValue && editsValue.TryGetValue<string>(out var editsJson))
        {
            try
            {
                var parsed = JsonNode.Parse(editsJson);
                if (parsed is JsonArray array) obj["edits"] = array;
                else if (IsSingleEdit(parsed)) obj["edits"] = new JsonArray { parsed };
            }
            catch (JsonException)
            {
                // Leave the string in place; schema validation will reject it.
            }
        }
        else if (IsSingleEdit(obj["edits"]))
        {
            obj["edits"] = new JsonArray { obj["edits"]!.DeepClone() };
        }

        if (obj["oldText"] is JsonValue oldValue && oldValue.TryGetValue<string>(out var oldText)
            && obj["newText"] is JsonValue newValue && newValue.TryGetValue<string>(out var newText))
        {
            var edits = obj["edits"] as JsonArray ?? [];
            edits.Add((JsonNode)new JsonObject { ["oldText"] = oldText, ["newText"] = newText });
            obj["edits"] = edits;
            obj.Remove("oldText");
            obj.Remove("newText");
        }

        return obj;
    }

    protected override Task<AgentToolResult> ExecuteAsync(
        string toolCallId, EditArgs args, Action<AgentToolResult>? onUpdate, CancellationToken cancellationToken)
    {
        if (args.Edits is null || args.Edits.Count == 0)
        {
            throw new InvalidOperationException("Edit tool input is invalid. edits must contain at least one replacement.");
        }

        var absolutePath = Resolve(args.Path);
        var edits = args.Edits.Select(e => new TextEdit(e.OldText, e.NewText)).ToList();

        return FileMutationQueue.RunAsync(absolutePath, async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(absolutePath))
            {
                throw new InvalidOperationException($"Could not edit file: {args.Path}. Error code: ENOENT.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            // Decode via Encoding.UTF8 so a leading BOM stays as U+FEFF, matching Node's buffer.toString("utf-8").
            var rawBytes = await File.ReadAllBytesAsync(absolutePath, cancellationToken).ConfigureAwait(false);
            ReadTracker?.EnsureFresh(absolutePath, rawBytes, args.Path);
            var rawContent = System.Text.Encoding.UTF8.GetString(rawBytes);
            cancellationToken.ThrowIfCancellationRequested();

            var (bom, content) = EditDiff.SplitBom(rawContent);
            var originalEnding = EditDiff.DetectLineEnding(content);
            var normalizedContent = EditDiff.NormalizeToLf(content);
            var applied = EditDiff.ApplyEditsToNormalizedContent(normalizedContent, edits, args.Path, fuzzy: args.Fuzzy == true);
            cancellationToken.ThrowIfCancellationRequested();

            var finalContent = bom + EditDiff.RestoreLineEndings(applied.NewContent, originalEnding);
            var finalBytes = WriteTool.Utf8NoBom.GetBytes(finalContent);
            await File.WriteAllBytesAsync(absolutePath, finalBytes, cancellationToken).ConfigureAwait(false);
            ReadTracker?.Record(absolutePath, finalBytes);
            cancellationToken.ThrowIfCancellationRequested();

            var diffResult = EditDiff.GenerateDiffString(applied.BaseContent, applied.NewContent);
            var patch = LineDiff.UnifiedPatch(args.Path, applied.BaseContent, applied.NewContent);
            var details = new JsonObject
            {
                ["diff"] = diffResult.Diff,
                ["patch"] = patch,
            };
            if (diffResult.FirstChangedLine is { } line) details["firstChangedLine"] = line;

            return AgentToolResult.FromText($"Successfully replaced {edits.Count} block(s) in {args.Path}.", details);
        }, cancellationToken);
    }

    private static bool IsSingleEdit(JsonNode? node)
    {
        if (node is not JsonObject obj || obj["oldText"] is null || obj["newText"] is null) return false;
        return obj["oldText"] is JsonValue oldV && oldV.TryGetValue<string>(out _)
            && obj["newText"] is JsonValue newV && newV.TryGetValue<string>(out _);
    }
}
