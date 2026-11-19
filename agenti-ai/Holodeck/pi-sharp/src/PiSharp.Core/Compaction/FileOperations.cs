// Ported from pi: packages/coding-agent/src/core/compaction/utils.ts @ 71dca871b
using System.Text.Json.Nodes;
using PiSharp.Core.Messages;

namespace PiSharp.Core.Compaction;

public sealed class FileOperations
{
    public HashSet<string> Read { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Written { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Edited { get; } = new(StringComparer.Ordinal);
}

public static class FileOpTracker
{
    public static void ExtractFromMessage(Message message, FileOperations fileOps)
    {
        if (message is not AssistantMessage assistant) return;
        foreach (var block in assistant.Content)
        {
            if (block is not ToolCallContent call) continue;
            if (call.Name == "apply_patch")
            {
                ExtractFromPatch(call, fileOps);
                continue;
            }
            if (call.Arguments["path"] is not JsonValue pathNode || !pathNode.TryGetValue<string>(out var path))
                continue;
            if (string.IsNullOrEmpty(path)) continue;
            switch (call.Name)
            {
                case "read":
                    fileOps.Read.Add(path);
                    break;
                case "write":
                    fileOps.Written.Add(path);
                    break;
                case "edit":
                    fileOps.Edited.Add(path);
                    break;
            }
        }
    }

    // apply_patch names its files inside the patch text, not in a path argument: every Add, Update, Delete and
    // Move target is a modified file, or the summary would lose every file a patch-editing seat touched.
    private static readonly string[] PatchHeaders = ["*** Add File: ", "*** Update File: ", "*** Delete File: ", "*** Move to: "];

    private static void ExtractFromPatch(ToolCallContent call, FileOperations fileOps)
    {
        var node = call.Arguments["input"] ?? call.Arguments["patch"];
        if (node is not JsonValue value || !value.TryGetValue<string>(out var patch)) return;
        foreach (var raw in patch.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            foreach (var header in PatchHeaders)
            {
                if (!line.StartsWith(header, StringComparison.Ordinal)) continue;
                var path = line[header.Length..].Trim();
                if (path.Length > 0) fileOps.Edited.Add(path);
                break;
            }
        }
    }

    public static (string[] ReadFiles, string[] ModifiedFiles) ComputeFileLists(FileOperations fileOps)
    {
        var modified = new HashSet<string>(fileOps.Edited, StringComparer.Ordinal);
        modified.UnionWith(fileOps.Written);
        var readOnly = fileOps.Read.Where(f => !modified.Contains(f)).OrderBy(f => f, StringComparer.Ordinal).ToArray();
        var modifiedFiles = modified.OrderBy(f => f, StringComparer.Ordinal).ToArray();
        return (readOnly, modifiedFiles);
    }

    public static string FormatFileOperations(IReadOnlyList<string> readFiles, IReadOnlyList<string> modifiedFiles)
    {
        var sections = new List<string>();
        if (readFiles.Count > 0)
            sections.Add($"<read-files>\n{string.Join("\n", readFiles)}\n</read-files>");
        if (modifiedFiles.Count > 0)
            sections.Add($"<modified-files>\n{string.Join("\n", modifiedFiles)}\n</modified-files>");
        return sections.Count == 0 ? "" : "\n\n" + string.Join("\n\n", sections);
    }
}
