using PiSharp.Core.Agent;
using System.Text.Json.Nodes;

namespace PiSharp.Tests.Tools;

/// <summary>Temporary directory cleaned up at the end of a test.</summary>
internal sealed class TempWorkspace : IDisposable
{
    public string Path { get; }

    public TempWorkspace()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pi-sharp-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string File(string relative, string content)
    {
        var full = System.IO.Path.Combine(Path, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
        var dir = System.IO.Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        System.IO.File.WriteAllText(full, content);
        return full;
    }

    public string Dir(string relative)
    {
        var full = System.IO.Path.Combine(Path, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
        Directory.CreateDirectory(full);
        return full;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (IOException) { /* best-effort */ }
    }
}

internal static class ToolRun
{
    public static Task<AgentToolResult> Exec(IAgentTool tool, string json, CancellationToken cancellationToken = default)
    {
        var args = JsonNode.Parse(json)!.AsObject();
        return tool.ExecuteAsync("t1", args, null, cancellationToken);
    }

    public static JsonObject Json(string json) => JsonNode.Parse(json)!.AsObject();
}
