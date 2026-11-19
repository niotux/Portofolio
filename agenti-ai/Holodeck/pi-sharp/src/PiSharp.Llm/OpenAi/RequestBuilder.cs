// Ported from pi: packages/ai/src/api/openai-completions.ts (buildParams, convertMessages, convertTools)
// @ 71dca871b, reduced to the generic OpenAI-compatible subset (llama.cpp, vLLM, LM Studio, Ollama).
using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Json;
using PiSharp.Core.Messages;
using PiSharp.Core.Models;

namespace PiSharp.Llm.OpenAi;

public static class RequestBuilder
{
    /// <summary>Token budgets per thinking level for servers that take an explicit budget field.</summary>
    public static int ThinkingBudget(ThinkingLevel level) => level switch
    {
        ThinkingLevel.Minimal => 1024,
        ThinkingLevel.Low => 2048,
        ThinkingLevel.Medium => 8192,
        _ => 16384, // high, xhigh and max clamp to high like pi does
    };

    public static string EffortName(ThinkingLevel level) => level switch
    {
        ThinkingLevel.Minimal => "minimal",
        ThinkingLevel.Low => "low",
        ThinkingLevel.Medium => "medium",
        ThinkingLevel.High => "high",
        ThinkingLevel.XHigh => "xhigh",
        ThinkingLevel.Max => "max",
        _ => "none",
    };

    /// <summary>Builds the JSON body for <c>POST /chat/completions</c>.</summary>
    public static JsonObject Build(ModelConfig model, LlmContext context, StreamOptions options)
    {
        var compat = model.Compat;
        var messages = MessageTransformer.Transform(model, context.Messages);

        var body = new JsonObject
        {
            ["model"] = model.Id,
            ["messages"] = ConvertMessages(model, context.SystemPrompt, messages),
            ["stream"] = true,
        };

        if (compat.SupportsUsageInStreaming)
        {
            body["stream_options"] = new JsonObject { ["include_usage"] = true };
        }

        var maxTokens = options.MaxTokens ?? model.MaxOutputTokens;
        if (maxTokens > 0) body[compat.MaxTokensField] = maxTokens;

        if (options.Temperature is { } temperature) body["temperature"] = temperature;

        if (context.Tools.Count > 0)
        {
            body["tools"] = ConvertTools(model, context.Tools);
        }
        else if (messages.Any(m => m is ToolResultMessage || m is AssistantMessage a && a.ToolCalls.Any()))
        {
            // Some templates reject tool_calls in the history when no tool list is sent.
            body["tools"] = new JsonArray();
        }

        ApplyToolChoice(model, context.Tools, options, body);
        ApplyThinking(model, options, body);

        return body;
    }

    private static void ApplyToolChoice(ModelConfig model, IReadOnlyList<ITool> tools, StreamOptions options, JsonObject body)
    {
        var choice = options.ToolChoice;
        if (string.IsNullOrEmpty(choice)) return;

        if (choice is "auto" or "none" or "required")
        {
            body["tool_choice"] = choice;
            return;
        }

        body["tool_choice"] = new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject { ["name"] = choice },
        };

        if (!model.Compat.SupportsJsonSchemaResponseFormat) return;
        var tool = tools.FirstOrDefault(t => t.Name == choice);
        if (tool is null) return;

        var strict = model.Compat.SupportsStrictMode == true;
        var schema = ConstrainedSampling.ToolCallEnvelopeSchema(tool, strict);
        if (strict) schema = ConstrainedSampling.TryMakeStrictJsonSchema(schema) ?? schema;
        body["response_format"] = ConstrainedSampling.JsonSchemaResponseFormat(choice, schema, strict);
    }

    private static void ApplyThinking(ModelConfig model, StreamOptions options, JsonObject body)
    {
        var compat = model.Compat;
        var level = options.Reasoning ?? ThinkingLevel.Off;
        var enabled = model.Reasoning && level != ThinkingLevel.Off;

        switch (compat.ThinkingFormat)
        {
            case ThinkingFormat.ReasoningEffort:
                if (enabled) body["reasoning_effort"] = EffortName(level);
                break;

            case ThinkingFormat.QwenChatTemplate:
                body["chat_template_kwargs"] = new JsonObject
                {
                    ["enable_thinking"] = enabled,
                    ["preserve_thinking"] = true,
                };
                break;

            case ThinkingFormat.ChatTemplate:
                if (compat.ChatTemplateKwargs is { } template)
                {
                    body["chat_template_kwargs"] = ResolveTemplateKwargs(template, enabled, level);
                }
                break;

            case ThinkingFormat.DeepSeek:
                body["thinking"] = new JsonObject { ["type"] = enabled ? "enabled" : "disabled" };
                if (enabled) body["reasoning_effort"] = EffortName(level);
                break;

            case ThinkingFormat.None:
            default:
                break;
        }

        if (enabled && compat.ThinkingTokenBudgetField is { Length: > 0 } budgetField)
        {
            body[budgetField] = ThinkingBudget(level);
        }
    }

    /// <summary>Replaces <c>"$thinking.enabled"</c> and <c>"$thinking.budget"</c> placeholders.</summary>
    private static JsonObject ResolveTemplateKwargs(JsonObject template, bool enabled, ThinkingLevel level)
    {
        var result = new JsonObject();
        foreach (var (key, value) in template)
        {
            result[key] = value switch
            {
                JsonValue v when v.TryGetValue(out string? s) && s == "$thinking.enabled" => JsonValue.Create(enabled),
                JsonValue v when v.TryGetValue(out string? s) && s == "$thinking.budget" => JsonValue.Create(enabled ? ThinkingBudget(level) : 0),
                JsonObject nested => ResolveTemplateKwargs(nested, enabled, level),
                null => null,
                _ => value.DeepClone(),
            };
        }
        return result;
    }

    public static JsonArray ConvertMessages(ModelConfig model, string systemPrompt, IReadOnlyList<Message> messages)
    {
        var compat = model.Compat;
        var result = new JsonArray();

        if (!string.IsNullOrEmpty(systemPrompt))
        {
            var role = model.Reasoning && compat.SupportsDeveloperRole ? "developer" : "system";
            result.Add((JsonNode)new JsonObject { ["role"] = role, ["content"] = systemPrompt });
        }

        for (var i = 0; i < messages.Count; i++)
        {
            var message = messages[i];
            switch (message)
            {
                case UserMessage user:
                    if (compat.RequiresAssistantAfterToolResult && i > 0 && messages[i - 1] is ToolResultMessage)
                    {
                        result.Add((JsonNode)new JsonObject { ["role"] = "assistant", ["content"] = "I have processed the tool results." });
                    }
                    var userJson = ConvertUser(model, user);
                    if (userJson is not null) result.Add((JsonNode)userJson);
                    break;

                case AssistantMessage assistant:
                    var assistantJson = ConvertAssistant(model, assistant);
                    if (assistantJson is not null) result.Add((JsonNode)assistantJson);
                    break;

                case ToolResultMessage:
                    // Consume the whole run of consecutive tool results so their images go into one user message.
                    var images = new List<ImageContent>();
                    while (i < messages.Count && messages[i] is ToolResultMessage toolResult)
                    {
                        result.Add((JsonNode)ConvertToolResult(compat, toolResult));
                        if (model.SupportsImages) images.AddRange(toolResult.Content.OfType<ImageContent>());
                        i++;
                    }
                    i--;
                    if (images.Count > 0)
                    {
                        var parts = new JsonArray { (JsonNode)new JsonObject { ["type"] = "text", ["text"] = "Attached image(s) from tool result:" } };
                        foreach (var image in images) parts.Add((JsonNode)ImagePart(image));
                        result.Add((JsonNode)new JsonObject { ["role"] = "user", ["content"] = parts });
                    }
                    break;

                default:
                    // Custom app messages must be mapped by ConvertToLlm before reaching the provider.
                    break;
            }
        }

        return result;
    }

    private static JsonObject? ConvertUser(ModelConfig model, UserMessage user)
    {
        var hasImages = model.SupportsImages && user.Content.Any(p => p is ImageContent);
        if (!hasImages)
        {
            var text = user.Text;
            if (text.Length == 0) return null;
            return new JsonObject { ["role"] = "user", ["content"] = text };
        }

        var parts = new JsonArray();
        foreach (var part in user.Content)
        {
            switch (part)
            {
                case TextContent { Text.Length: > 0 } t:
                    parts.Add((JsonNode)new JsonObject { ["type"] = "text", ["text"] = t.Text });
                    break;
                case ImageContent img:
                    parts.Add((JsonNode)ImagePart(img));
                    break;
            }
        }
        if (parts.Count == 0) return null;
        return new JsonObject { ["role"] = "user", ["content"] = parts };
    }

    private static JsonObject ImagePart(ImageContent image) => new()
    {
        ["type"] = "image_url",
        ["image_url"] = new JsonObject { ["url"] = $"data:{image.MimeType};base64,{image.Data}" },
    };

    private static JsonObject? ConvertAssistant(ModelConfig model, AssistantMessage assistant)
    {
        var compat = model.Compat;
        var json = new JsonObject { ["role"] = "assistant" };

        var textParts = assistant.Content.OfType<TextContent>().Where(t => t.Text.Length > 0).Select(t => t.Text).ToList();
        var thinkingParts = assistant.Content.OfType<ThinkingContent>().Where(t => !t.Redacted && t.Thinking.Length > 0).ToList();
        var toolCalls = assistant.ToolCalls.ToList();

        var hasContent = false;
        if (thinkingParts.Count > 0 && compat.RequiresThinkingAsText)
        {
            var parts = new JsonArray();
            parts.Add((JsonNode)new JsonObject { ["type"] = "text", ["text"] = string.Join("\n", thinkingParts.Select(t => t.Thinking)) });
            foreach (var text in textParts) parts.Add((JsonNode)new JsonObject { ["type"] = "text", ["text"] = text });
            json["content"] = parts;
            hasContent = true;
        }
        else
        {
            if (textParts.Count > 0)
            {
                json["content"] = string.Join("", textParts);
                hasContent = true;
            }
            else
            {
                json["content"] = compat.RequiresAssistantAfterToolResult ? "" : null;
            }

            if (thinkingParts.Count > 0)
            {
                var field = thinkingParts[0].Signature is { Length: > 0 } sig ? sig : "reasoning_content";
                json[field] = string.Join("\n", thinkingParts.Select(t => t.Thinking));
            }
        }

        if (toolCalls.Count > 0)
        {
            var calls = new JsonArray();
            foreach (var call in toolCalls)
            {
                calls.Add((JsonNode)new JsonObject
                {
                    ["id"] = call.Id,
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = call.Name,
                        ["arguments"] = call.Arguments.ToJsonString(CoreJsonContext.Default.Options),
                    },
                });
            }
            json["tool_calls"] = calls;
        }

        if (compat.RequiresReasoningContentOnAssistantMessages && model.Reasoning && !json.ContainsKey("reasoning_content"))
        {
            json["reasoning_content"] = "";
        }

        if (!hasContent && toolCalls.Count == 0) return null;
        return json;
    }

    private static JsonObject ConvertToolResult(ModelCompat compat, ToolResultMessage result)
    {
        var text = string.Join("\n", result.Content.OfType<TextContent>().Select(t => t.Text));
        if (text.Length == 0)
        {
            text = result.Content.Any(p => p is ImageContent) ? "(see attached image)" : "(no tool output)";
        }

        var json = new JsonObject
        {
            ["role"] = "tool",
            ["content"] = text,
            ["tool_call_id"] = result.ToolCallId,
        };
        if (compat.RequiresToolResultName) json["name"] = result.ToolName;
        return json;
    }

    public static JsonArray ConvertTools(ModelConfig model, IReadOnlyList<ITool> tools)
    {
        var result = new JsonArray();
        var includeStrictField = model.Compat.SupportsStrictMode != false;
        var forceStrict = model.Compat.SupportsStrictMode == true;
        foreach (var tool in tools)
        {
            var strict = forceStrict && ConstrainedSampling.ResolveStrictSampling(tool, supportsStrictMode: true);
            var parameters = ConstrainedSampling.GetJsonSchemaToolParameters(tool, strict);
            var function = new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["parameters"] = parameters,
            };
            if (includeStrictField) function["strict"] = strict;
            result.Add((JsonNode)new JsonObject { ["type"] = "function", ["function"] = function });
        }
        return result;
    }
}
