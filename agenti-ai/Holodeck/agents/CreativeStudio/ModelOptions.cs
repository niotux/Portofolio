namespace CreativeStudio;

public sealed record ModelOptions(string BaseUrl, string Name, string ApiKey, double Temperature, int MaxOutputTokens, int TimeoutSeconds);
