namespace Humans;

public sealed record AgentSettings(Personality Personality, ModelOptions Model, EmbeddingOptions? Embedding = null);
