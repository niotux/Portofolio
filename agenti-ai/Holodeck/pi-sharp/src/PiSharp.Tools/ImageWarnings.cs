namespace PiSharp.Tools;

/// <summary>Builds the model-facing warning for an image that is large for the configured limits.</summary>
public static class ImageWarnings
{
    public const int DefaultWarnPixels = 4_000_000;
    public const int MaxEstimatedTokens = 16_384;

    public static string? For(
        ReadOnlySpan<byte> data,
        int warnPixels = DefaultWarnPixels,
        int contextWindow = 0)
    {
        if (warnPixels <= 0 || !ImageSniffer.TryGetDimensions(data, out var width, out var height))
            return null;

        var pixels = (long)width * height;
        var estimatedTokens = Math.Min(MaxEstimatedTokens, (pixels + 1023) / 1024);
        var overPixelLimit = pixels > warnPixels;
        var overContextLimit = contextWindow > 0 && estimatedTokens > contextWindow * 0.25;
        if (!overPixelLimit && !overContextLimit) return null;

        var tokenText = estimatedTokens >= 1000
            ? $"{estimatedTokens / 1000d:0.#}k"
            : estimatedTokens.ToString();
        return $"note: image is {width}x{height} (~{tokenText} tokens). If detail is not needed, crop or downscale it with bash before reading.";
    }
}
