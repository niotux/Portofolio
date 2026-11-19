// Ported from pi packages/ai/src/api/openai-responses-shared.ts and
// {openai,openai-codex}-responses.ts @ 71dca871b. Function tools and SSE subset.
using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Messages;
using PiSharp.Core.Models;
using PiSharp.Llm.OpenAi;

namespace PiSharp.Llm.Responses;

public static class ResponsesRequest
{
    public static JsonObject Build(ModelConfig model, LlmContext context, StreamOptions options)
    {
        var codex = model.Provider == "openai-codex";
        if (!codex && model.Provider != "xai") throw new ArgumentException("Unknown subscription provider.");
        var input = new JsonArray();
        if (!codex && context.SystemPrompt.Length > 0)
            input.Add((JsonNode)new JsonObject { ["role"] = model.Reasoning ? "developer" : "system", ["content"] = context.SystemPrompt });
        var pending = new HashSet<string>(StringComparer.Ordinal);
        var freeform = Freeform(model, options);
        var custom = new HashSet<string>(StringComparer.Ordinal);
        // A server compaction item holds everything before it: requests start there (the API's own advice).
        foreach (var message in MessageTransformer.Transform(model, PiSharp.Core.Compaction.ServerCompaction.FromLatest(context.Messages, model.Id, model.Provider)))
        {
            switch (message)
            {
                case UserMessage user:
                    input.Add((JsonNode)new JsonObject { ["role"] = "user", ["content"] = Parts(user.Content) });
                    break;
                case AssistantMessage assistant:
                    var same = assistant.Provider == model.Provider && assistant.Model == model.Id;
                    foreach (var part in assistant.Content)
                    {
                        switch (part)
                        {
                            case ThinkingContent thinking when same && thinking.Signature is not null:
                                var reasoning = JsonNode.Parse(thinking.Signature) as JsonObject;
                                if (reasoning?["type"]?.GetValue<string>() is "reasoning" || PiSharp.Core.Compaction.ServerCompaction.IsItemType(reasoning?["type"]?.GetValue<string>()))
                                    input.Add((JsonNode)reasoning!);
                                break;
                            case TextContent text:
                                var item = new JsonObject
                                {
                                    ["type"] = "message", ["role"] = "assistant", ["status"] = "completed",
                                    ["content"] = new JsonArray(new JsonObject { ["type"] = "output_text", ["text"] = text.Text, ["annotations"] = new JsonArray() }),
                                };
                                if (same && text.Signature is not null && JsonNode.Parse(text.Signature) is JsonObject signature)
                                {
                                    item["id"] = signature["id"]?.DeepClone();
                                    if (signature["phase"] is { } phase) item["phase"] = phase.DeepClone();
                                }
                                input.Add((JsonNode)item);
                                break;
                            case ToolCallContent call:
                                var ids = call.Id.Split('|', 2);
                                var callId = CallId(ids[0]);
                                if (freeform.Contains(call.Name) && call.Arguments["input"] is JsonValue freeInput && freeInput.TryGetValue<string>(out var patchText))
                                {
                                    // A free-form tool's call goes back as the custom call the model made (Codex's apply_patch).
                                    var customCall = new JsonObject
                                    {
                                        ["type"] = "custom_tool_call", ["call_id"] = callId, ["name"] = call.Name, ["input"] = patchText,
                                    };
                                    if (same && ids.Length == 2 && ids[1].StartsWith("ctc_", StringComparison.Ordinal)) customCall["id"] = ids[1];
                                    input.Add((JsonNode)customCall);
                                    custom.Add(callId);
                                    pending.Add(callId);
                                    break;
                                }
                                var function = new JsonObject
                                {
                                    ["type"] = "function_call", ["call_id"] = callId,
                                    ["name"] = call.Name, ["arguments"] = call.Arguments.ToJsonString(),
                                };
                                if (same && ids.Length == 2 && ids[1].StartsWith("fc_", StringComparison.Ordinal)) function["id"] = ids[1];
                                input.Add((JsonNode)function);
                                pending.Add(callId);
                                break;
                        }
                    }
                    break;
                case ToolResultMessage result:
                    var resultId = CallId(result.ToolCallId.Split('|', 2)[0]);
                    if (!pending.Remove(resultId)) break; // Never replay orphan results from a failed turn.
                    input.Add((JsonNode)new JsonObject
                    {
                        ["type"] = custom.Contains(resultId) ? "custom_tool_call_output" : "function_call_output",
                        ["call_id"] = resultId, ["output"] = Parts(result.Content),
                    });
                    break;
            }
        }
        var body = new JsonObject
        {
            ["model"] = model.Id, ["store"] = false, ["stream"] = true, ["input"] = input,
            ["include"] = new JsonArray("reasoning.encrypted_content"),
        };
        if (codex)
        {
            body["instructions"] = string.IsNullOrEmpty(context.SystemPrompt) ? "You are a helpful assistant." : context.SystemPrompt;
            body["text"] = new JsonObject { ["verbosity"] = "low" };
            body["parallel_tool_calls"] = true;
            if (SubscriptionResponsesClient.ValidCacheKey(options.CacheKey)) body["prompt_cache_key"] = options.CacheKey;
            if (options.ServerCompactThreshold is > 0 and var threshold)
                body["context_management"] = new JsonArray(new JsonObject { ["type"] = "compaction", ["compact_threshold"] = threshold });
        }
        else body["max_output_tokens"] = Math.Max(16, options.MaxTokens ?? model.MaxOutputTokens);
        if (options.Temperature is { } temperature) body["temperature"] = temperature;
        if (model.Reasoning && options.Reasoning is { } level && level != ThinkingLevel.Off)
            body["reasoning"] = new JsonObject { ["effort"] = level.ToString().ToLowerInvariant(), ["summary"] = "auto" };
        if (context.Tools.Count > 0)
        {
            body["tools"] = new JsonArray(context.Tools.Select(t => freeform.Contains(t.Name)
                ? (JsonNode)new JsonObject
                {
                    ["type"] = "custom", ["name"] = t.Name, ["description"] = ApplyPatchFreeformDescription,
                    ["format"] = new JsonObject { ["type"] = "grammar", ["syntax"] = "lark", ["definition"] = ApplyPatchGrammar },
                }
                : (JsonNode)new JsonObject
                {
                    ["type"] = "function", ["name"] = t.Name, ["description"] = t.Description,
                    ["parameters"] = t.Parameters.DeepClone(), ["strict"] = false,
                }).ToArray());
            body["tool_choice"] = options.ToolChoice is not null and not ("auto" or "none" or "required")
                ? new JsonObject { ["type"] = "function", ["name"] = options.ToolChoice }
                : JsonValue.Create(options.ToolChoice ?? "auto");
        }
        return body;
    }

    /// <summary>The tools this request sends free-form: only on the Codex backend, and only apply_patch (the one with a grammar).</summary>
    private static IReadOnlySet<string> Freeform(ModelConfig model, StreamOptions options) =>
        model.Provider == "openai-codex" && options.FreeformTools is { } names && names.Contains("apply_patch")
            ? new HashSet<string>(StringComparer.Ordinal) { "apply_patch" } : new HashSet<string>(StringComparer.Ordinal);

    public const string ApplyPatchFreeformDescription =
        "Use the `apply_patch` tool to edit files. This is a FREEFORM tool, so do not wrap the patch in JSON.";

    /// <summary>The apply_patch grammar GPT models are trained on (Lark), as OpenAI Codex sends it (codex-rs
    /// core/assets/tools/apply_patch.lark, Apache-2.0).</summary>
    public const string ApplyPatchGrammar = """
        start: begin_patch hunk+ end_patch
        begin_patch: "*** Begin Patch" LF
        end_patch: "*** End Patch" LF?

        hunk: add_hunk | delete_hunk | update_hunk
        add_hunk: "*** Add File: " filename LF add_line+
        delete_hunk: "*** Delete File: " filename LF
        update_hunk: "*** Update File: " filename LF change_move? change?

        filename: /(.+)/
        add_line: "+" /(.*)/ LF -> line

        change_move: "*** Move to: " filename LF
        change: (change_context | change_line)+ eof_line?
        change_context: ("@@" | "@@ " /(.+)/) LF
        change_line: ("+" | "-" | " ") /(.*)/ LF
        eof_line: "*** End of File" LF

        %import common.LF
        """;

    private static JsonArray Parts(IReadOnlyList<ContentPart> parts)
    {
        var result = new JsonArray();
        foreach (var part in parts)
            if (part is TextContent text) result.Add((JsonNode)new JsonObject { ["type"] = "input_text", ["text"] = text.Text });
            else if (part is ImageContent image) result.Add((JsonNode)new JsonObject
            {
                ["type"] = "input_image", ["image_url"] = $"data:{image.MimeType};base64,{image.Data}", ["detail"] = "auto",
            });
        if (result.Count == 0) result.Add((JsonNode)new JsonObject { ["type"] = "input_text", ["text"] = "(empty result)" });
        return result;
    }

    private static string CallId(string id)
        => new string(id.Select(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' ? c : '_').Take(64).ToArray());
}
