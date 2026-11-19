// Base for the built-in tools. pi creates each tool with a cwd and lets an extension context
// override it per call (packages/coding-agent/src/core/tools/*.ts @ 71dca871b); pi-sharp has no
// extensions, so the working directory is a mutable property on the tool instead.
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using PiSharp.Core.Agent;

namespace PiSharp.Tools;

/// <summary>A built-in tool bound to a working directory.</summary>
public abstract class BuiltinTool<TArgs> : AgentTool<TArgs>
{
    protected BuiltinTool(
        string name,
        string label,
        string description,
        JsonObject parameters,
        JsonTypeInfo<TArgs> argsTypeInfo,
        string cwd)
        : base(name, label, description, parameters, argsTypeInfo)
    {
        Cwd = cwd;
    }

    /// <summary>Directory relative tool paths resolve against.</summary>
    public string Cwd { get; set; }

    /// <summary>
    /// Read-before-edit guard shared by one session's tools. Null (a tool built on its own) = no guard;
    /// <see cref="BuiltinTools.Create"/> wires one into read, edit, write and apply_patch.
    /// </summary>
    public FileReadTracker? ReadTracker { get; set; }
    public override ToolExecutionMode? ExecutionMode => Name is "read" or "grep" or "find" or "ls"
        ? ToolExecutionMode.Parallel : ToolExecutionMode.Sequential;
    public override ToolAccess? Access(JsonObject arguments) => Name is "read" or "grep" or "find" or "ls" or "edit" or "write"
        ? new ToolAccess(Name is "read" or "grep" or "find" or "ls", [Resolve(arguments["path"]?.GetValue<string>() ?? ".")]) : null;

    /// <summary>Resolve a tool path argument against <see cref="Cwd"/>.</summary>
    protected string Resolve(string path) => ToolPaths.ResolveToCwd(path, Cwd);
}
