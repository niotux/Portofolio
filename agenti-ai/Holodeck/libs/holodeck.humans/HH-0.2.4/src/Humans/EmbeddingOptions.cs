namespace Humans;

public sealed record EmbeddingOptions(string BaseUrl, string Name, int Dimensions, int TimeoutSeconds);
