// Ported from pi: packages/ai/src/utils/estimate.ts @ 71dca871b
using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Messages;

namespace PiSharp.Core.Session;

public sealed record ContextUsageEstimate(
    int Tokens,
    int UsageTokens,
    int TrailingTokens,
    int? LastUsageIndex);

/// <summary>Chars/4 token heuristic with last-assistant usage when it still describes the prefix.</summary>
public static class TokenEstimate
{
    public const int CharsPerToken = 4;
    public const int EstimatedImageChars = 4800;

    public static int CalculateContextTokens(Usage usage) =>
        usage.TotalTokens != 0
            ? usage.TotalTokens
            : usage.Input + usage.Output + usage.CacheRead + usage.CacheWrite;

    public static int EstimateTextTokens(string text) =>
        (int)Math.Ceiling(text.Length / (double)CharsPerToken);

    public static int EstimateMessageTokens(Message message)
    {
        switch (message)
        {
            case UserMessage user:
                return EstimateTextAndImageTokens(user.Content);
            case ToolResultMessage tool:
                return EstimateTextAndImageTokens(tool.Content);
            case CompactionSummaryMessage compaction:
                return EstimateTextTokens(compaction.Summary);
            case AssistantMessage assistant:
            {
                var chars = 0;
                foreach (var block in assistant.Content)
                {
                    chars += block switch
                    {
                        TextContent text => text.Text.Length,
                        ThinkingContent thinking => thinking.Thinking.Length,
                        ImageContent => EstimatedImageChars,
                        ToolCallContent call => call.Name.Length + SafeJson(call.Arguments).Length,
                        _ => 0,
                    };
                }
                return (int)Math.Ceiling(chars / (double)CharsPerToken);
            }
            default:
                return 0;
        }
    }

    public static ContextUsageEstimate EstimateContextTokens(IReadOnlyList<Message> messages) =>
        EstimateMessages(messages);

    public static ContextUsageEstimate EstimateContextTokens(LlmContext context, string? model = null, string? provider = null)
    {
        var estimate = EstimateMessages(context.Messages, model, provider);
        if (estimate.LastUsageIndex is { } last)
        {
            var addedNames = new HashSet<string>(StringComparer.Ordinal);
            for (var i = last + 1; i < context.Messages.Count; i++)
            {
                if (context.Messages[i] is ToolResultMessage { AddedToolNames: { } names })
                {
                    foreach (var name in names) addedNames.Add(name);
                }
            }

            var addedToolTokens = EstimateToolsTokens(context.Tools.Where(t => addedNames.Contains(t.Name)));
            return estimate with
            {
                Tokens = estimate.Tokens + addedToolTokens,
                TrailingTokens = estimate.TrailingTokens + addedToolTokens,
            };
        }

        var prefixTokens = (string.IsNullOrEmpty(context.SystemPrompt) ? 0 : EstimateTextTokens(context.SystemPrompt))
            + EstimateToolsTokens(context.Tools);
        return estimate with
        {
            Tokens = estimate.Tokens + prefixTokens,
            TrailingTokens = estimate.TrailingTokens + prefixTokens,
        };
    }

    private static ContextUsageEstimate EstimateMessages(IReadOnlyList<Message> messages, string? model = null, string? provider = null)
    {
        var usageInfo = GetLastAssistantUsageInfo(messages, model, provider);
        if (usageInfo is { } info)
        {
            var usageTokens = CalculateContextTokens(info.Usage);
            var trailing = 0;
            for (var i = info.Index + 1; i < messages.Count; i++)
                trailing += EstimateMessageTokens(messages[i]);
            return new(usageTokens + trailing, usageTokens, trailing, info.Index);
        }

        var tokens = 0;
        foreach (var message in messages) tokens += EstimateMessageTokens(message);
        return new(tokens, 0, tokens, null);
    }

    private static (Usage Usage, int Index)? GetLastAssistantUsageInfo(IReadOnlyList<Message> messages, string? model, string? provider)
    {
        var latestPrefixTimestamp = DateTimeOffset.MinValue;
        (Usage Usage, int Index)? usageInfo = null;

        for (var i = 0; i < messages.Count; i++)
        {
            var message = messages[i];
            if (message is AssistantMessage assistant)
            {
                var usageAppliesToPrefix = assistant.Timestamp >= latestPrefixTimestamp;
                if (usageAppliesToPrefix && (model is null || assistant.Model == model) && (provider is null || assistant.Provider == provider)
                    && assistant.StopReason is not StopReason.Aborted and not StopReason.Error
                    && CalculateContextTokens(assistant.Usage) > 0)
                {
                    usageInfo = (assistant.Usage, i);
                }
            }

            if (message.Timestamp > latestPrefixTimestamp) latestPrefixTimestamp = message.Timestamp;
        }

        return usageInfo;
    }

    private static int EstimateTextAndImageTokens(IReadOnlyList<ContentPart> content)
    {
        var chars = 0;
        foreach (var block in content)
        {
            chars += block switch
            {
                TextContent text => text.Text.Length,
                ImageContent => EstimatedImageChars,
                _ => 0,
            };
        }
        return (int)Math.Ceiling(chars / (double)CharsPerToken);
    }

    private static int EstimateToolsTokens(IEnumerable<ITool> tools)
    {
        var array = new JsonArray();
        var any = false;
        foreach (var tool in tools)
        {
            any = true;
            array.Add((JsonNode)new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["parameters"] = tool.Parameters.DeepClone(),
            });
        }
        return any ? EstimateTextTokens(array.ToJsonString()) : 0;
    }

    private static string SafeJson(JsonNode? value)
    {
        try
        {
            return value?.ToJsonString() ?? "null";
        }
        catch (Exception)
        {
            return "[unserializable]";
        }
    }
}
