using System.Text.Json.Nodes;

namespace CreativeStudio;

public sealed record GenerationPlan(string PromptId, string WorkflowId, string WorkflowRevision, string GraphHash, GenerationInput Input, JsonObject Graph, List<WorkflowArtifact> Artifacts);
