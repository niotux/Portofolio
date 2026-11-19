namespace CreativeStudio;

public sealed record ComfyOptions(string BaseUrl, int RequestTimeoutSeconds, int PollSeconds);
