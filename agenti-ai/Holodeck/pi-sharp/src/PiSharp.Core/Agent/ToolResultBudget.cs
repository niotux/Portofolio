using PiSharp.Core.Messages;
using PiSharp.Core.Session;
using System.Text.Json.Nodes;

namespace PiSharp.Core.Agent;

/// <summary>Bounds text from a tool result before it is retained in the conversation history.</summary>
internal static class ToolResultBudget
{
    internal const int DetailsByteBudget = 64 * 1024;

    internal static JsonNode? ApplyDetails(JsonNode? details)
    {
        if (details is null) return null;
        var json = details.ToJsonString();
        var bytes = System.Text.Encoding.UTF8.GetByteCount(json);
        return bytes <= DetailsByteBudget
            ? details
            : new JsonObject { ["truncated"] = true, ["originalBytes"] = bytes };
    }

    internal static IReadOnlyList<ContentPart> Apply(IReadOnlyList<ContentPart> content, int budget)
    {
        if (budget <= 0) return content;
        var textParts = content.OfType<TextContent>().ToArray();
        var fullText = string.Concat(textParts.Select(p => p.Text));
        if (TokenEstimate.EstimateTextTokens(fullText) <= budget) return content;

        var path = Path.Combine(Path.GetTempPath(), $"pi-sharp-tool-result-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, fullText);
        var originalTokens = TokenEstimate.EstimateTextTokens(fullText);
        var lines = fullText.Count(c => c == '\n') + (fullText.Length == 0 || fullText[^1] == '\n' ? 0 : 1);
        var marker = "";
        var keepChars = 0;
        for (var i = 0; i < 3; i++)
        {
            keepChars = Math.Max(0, budget * TokenEstimate.CharsPerToken - marker.Length);
            var retainedTokens = TokenEstimate.EstimateTextTokens(new string('x', keepChars));
            marker = $"[... {Math.Max(0, originalTokens - retainedTokens)} tokens omitted; original {originalTokens} tokens, {lines} lines; full output: {path}]";
        }
        keepChars = Math.Max(0, budget * TokenEstimate.CharsPerToken - marker.Length);
        var headChars = keepChars / 2;
        var tailChars = keepChars - headChars;
        var clipped = fullText[..Math.Min(headChars, fullText.Length)] + marker +
            (tailChars == 0 ? "" : fullText[Math.Max(headChars, fullText.Length - tailChars)..]);

        var result = new List<ContentPart>(content.Count);
        var inserted = false;
        foreach (var part in content)
        {
            if (part is TextContent)
            {
                if (!inserted) { result.Add(new TextContent(clipped)); inserted = true; }
            }
            else result.Add(part);
        }
        if (!inserted) result.Insert(0, new TextContent(clipped));
        return result;
    }
}
