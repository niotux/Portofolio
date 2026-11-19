namespace CreativeStudio;

public sealed record EmbeddingOptions(string BaseUrl, string Name, string ApiKey, int Dimensions, int TimeoutSeconds);
