using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace CreativeStudio;

public sealed record ComfyPromptRequest(JsonObject Prompt, [property: JsonPropertyName("prompt_id")] string PromptId, [property: JsonPropertyName("client_id")] string ClientId);
