// Ported from pi: packages/coding-agent/src/core/tools/write.ts @ 71dca871b
using System.Text;
using System.Text.Json.Nodes;
using PiSharp.Core.Agent;

namespace PiSharp.Tools;

/// <summary>Create or overwrite a file, creating parent directories as needed.</summary>
public sealed class WriteTool : BuiltinTool<WriteArgs>
{
    private static readonly JsonObject ArgsSchema = Schema.Parse("""
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "Path to the file to write (relative or absolute)" },
            "content": { "type": "string", "description": "Content to write to the file" }
          },
          "required": ["path", "content"]
        }
        """);

    private const string Describe =
        "Write content to a file. Creates the file if it doesn't exist, overwrites if it does. " +
        "Automatically creates parent directories.";

    public WriteTool(string cwd)
        : base("write", "write", Describe, ArgsSchema, ToolsJsonContext.Default.WriteArgs, cwd)
    {
    }

    /// <summary>UTF-8 without a byte order mark, like Node's <c>writeFile(path, content, "utf-8")</c>.</summary>
    internal static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    protected override Task<AgentToolResult> ExecuteAsync(
        string toolCallId, WriteArgs args, Action<AgentToolResult>? onUpdate, CancellationToken cancellationToken)
    {
        var absolutePath = Resolve(args.Path);
        var dir = Path.GetDirectoryName(absolutePath);

        return FileMutationQueue.RunAsync(absolutePath, async () =>
        {
            // Cancellation is checked between steps rather than through a callback, so the queue
            // slot is never released while a filesystem operation is still in flight.
            cancellationToken.ThrowIfCancellationRequested();
            // A new file needs no prior read; overwriting one does, against what is on disk now.
            if (ReadTracker is not null && File.Exists(absolutePath))
            {
                var current = await File.ReadAllBytesAsync(absolutePath, cancellationToken).ConfigureAwait(false);
                ReadTracker.EnsureFresh(absolutePath, current, args.Path);
            }
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            cancellationToken.ThrowIfCancellationRequested();

            var bytes = Utf8NoBom.GetBytes(args.Content);
            await File.WriteAllBytesAsync(absolutePath, bytes, cancellationToken).ConfigureAwait(false);
            ReadTracker?.Record(absolutePath, bytes);
            cancellationToken.ThrowIfCancellationRequested();

            return AgentToolResult.FromText($"Successfully wrote to {args.Path}");
        }, cancellationToken);
    }
}
