using System.Text.Json.Serialization;

namespace CreativeStudio;

public sealed record ComfyPromptResponse([property: JsonPropertyName("prompt_id")] string PromptId);
