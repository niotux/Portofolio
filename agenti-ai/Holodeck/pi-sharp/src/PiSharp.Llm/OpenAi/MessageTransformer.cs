// Ported from pi: packages/ai/src/api/transform-messages.ts @ 71dca871b
// Normalizes a transcript before it is converted to the wire format:
// - images become text placeholders for text-only models,
// - thinking from another model is dropped (the server cannot replay it),
// - assistant turns that ended in error/abort are skipped,
// - tool calls without a result get a synthesized error result so the history stays well-formed.
using PiSharp.Core.Messages;
using PiSharp.Core.Models;

namespace PiSharp.Llm.OpenAi;

public static class MessageTransformer
{
    public const string ImagePlaceholder = "(image omitted: model does not support images)";
    public const string ToolImagePlaceholder = "(tool image omitted: model does not support images)";
    public const string MissingResultText = "No result provided";

    public static List<Message> Transform(ModelConfig model, IReadOnlyList<Message> messages)
    {
        var supportsImages = model.SupportsImages;
        var firstPass = new List<Message>(messages.Count);

        foreach (var message in messages)
        {
            switch (message)
            {
                case UserMessage user:
                    var userParts = ImageValidation.Project(user.Content, $"user {user.Timestamp:O}", out _);
                    firstPass.Add(user with { Content = supportsImages ? userParts : DowngradeImages(userParts, ImagePlaceholder) });
                    break;

                case ToolResultMessage result:
                    var toolParts = ImageValidation.Project(result.Content, $"tool {result.ToolName} call {result.ToolCallId}", out var rejected);
                    firstPass.Add(result with { Content = supportsImages ? toolParts : DowngradeImages(toolParts, ToolImagePlaceholder), IsError = result.IsError || rejected });
                    break;

                case AssistantMessage assistant:
                    firstPass.Add(NormalizeThinking(model, assistant));
                    break;

                default:
                    firstPass.Add(message);
                    break;
            }
        }

        // Second pass: drop failed assistant turns and repair orphaned tool calls.
        var output = new List<Message>(firstPass.Count);
        var pending = new List<ToolCallContent>();

        void FlushPending()
        {
            foreach (var call in pending)
            {
                output.Add(new ToolResultMessage
                {
                    ToolCallId = call.Id,
                    ToolName = call.Name,
                    Content = [new TextContent(MissingResultText)],
                    IsError = true,
                });
            }
            pending.Clear();
        }

        foreach (var message in firstPass)
        {
            switch (message)
            {
                case AssistantMessage assistant:
                    if (assistant.StopReason is StopReason.Error or StopReason.Aborted) continue;
                    FlushPending();
                    output.Add(assistant);
                    pending.AddRange(assistant.ToolCalls);
                    break;

                case ToolResultMessage result:
                    pending.RemoveAll(c => c.Id == result.ToolCallId);
                    output.Add(result);
                    break;

                case UserMessage:
                    FlushPending();
                    output.Add(message);
                    break;

                default:
                    output.Add(message);
                    break;
            }
        }
        FlushPending();

        return output;
    }

    private static IReadOnlyList<ContentPart> DowngradeImages(IReadOnlyList<ContentPart> content, string placeholder)
    {
        if (!content.Any(p => p is ImageContent)) return content;

        var result = new List<ContentPart>(content.Count);
        var lastWasImage = false;
        foreach (var part in content)
        {
            if (part is ImageContent)
            {
                if (!lastWasImage) result.Add(new TextContent(placeholder));
                lastWasImage = true;
                continue;
            }
            lastWasImage = false;
            result.Add(part);
        }
        return result;
    }

    private static AssistantMessage NormalizeThinking(ModelConfig model, AssistantMessage assistant)
    {
        if (!assistant.Content.Any(p => p is ThinkingContent)) return assistant;

        var sameModel = assistant.Model == model.Id;
        var content = new List<ContentPart>(assistant.Content.Count);
        foreach (var part in assistant.Content)
        {
            if (part is not ThinkingContent thinking)
            {
                content.Add(part);
                continue;
            }

            if (thinking.Redacted)
            {
                if (sameModel) content.Add(thinking);
                continue;
            }

            if (sameModel)
            {
                if (thinking.Signature is not null || thinking.Thinking.Length > 0) content.Add(thinking);
                continue;
            }

            // Another model produced this reasoning: keep it as plain text so the context is not lost.
            if (thinking.Thinking.Length > 0) content.Add(new TextContent(thinking.Thinking));
        }
        return assistant with { Content = content };
    }
}
