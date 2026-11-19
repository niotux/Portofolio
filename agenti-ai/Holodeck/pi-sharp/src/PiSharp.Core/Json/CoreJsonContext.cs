// Source-generated JSON metadata for Core types (no reflection, Native AOT safe).
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using PiSharp.Core.Agent;
using PiSharp.Core.Mcp;
using PiSharp.Core.Messages;
using PiSharp.Core.Models;
using PiSharp.Core.Session;
using PiSharp.Core.Skills;

namespace PiSharp.Core.Json;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(Message))]
[JsonSerializable(typeof(UserMessage))]
[JsonSerializable(typeof(AssistantMessage))]
[JsonSerializable(typeof(ToolResultMessage))]
[JsonSerializable(typeof(CompactionSummaryMessage))]
[JsonSerializable(typeof(List<Message>))]
[JsonSerializable(typeof(ContentPart))]
[JsonSerializable(typeof(Usage))]
[JsonSerializable(typeof(CompactionDetails))]
[JsonSerializable(typeof(ModelConfig))]
[JsonSerializable(typeof(List<ModelConfig>))]
[JsonSerializable(typeof(ModelCompat))]
[JsonSerializable(typeof(AgentToolResult))]
[JsonSerializable(typeof(JsonObject))]
[JsonSerializable(typeof(JsonNode))]
[JsonSerializable(typeof(McpConfigFile))]
[JsonSerializable(typeof(McpServerEntry))]
[JsonSerializable(typeof(Dictionary<string, McpServerEntry>))]
[JsonSerializable(typeof(SkillLockFile))]
[JsonSerializable(typeof(SkillLockEntry))]
[JsonSerializable(typeof(SkillLockFileEntry))]
[JsonSerializable(typeof(List<SkillLockEntry>))]
[JsonSerializable(typeof(List<SkillLockFileEntry>))]
public sealed partial class CoreJsonContext : JsonSerializerContext
{
    /// <summary>Indented variant for files a human may read (sessions, settings).</summary>
    private static CoreJsonContext? s_indented;
    public static CoreJsonContext Indented => s_indented ??= new(new JsonSerializerOptions(Default.Options) { WriteIndented = true });
}
