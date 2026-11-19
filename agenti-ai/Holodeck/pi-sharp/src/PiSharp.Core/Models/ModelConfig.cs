// Replaces pi: packages/ai/src/types.ts (Model) and OpenAICompletionsCompat, reduced to the
// knobs listed in docs/02-library-map.md. Plain data; interpretation lives in PiSharp.Llm.
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace PiSharp.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter<ThinkingLevel>))]
public enum ThinkingLevel
{
    [JsonStringEnumMemberName("off")] Off,
    [JsonStringEnumMemberName("minimal")] Minimal,
    [JsonStringEnumMemberName("low")] Low,
    [JsonStringEnumMemberName("medium")] Medium,
    [JsonStringEnumMemberName("high")] High,
    [JsonStringEnumMemberName("xhigh")] XHigh,
    [JsonStringEnumMemberName("max")] Max,
}

[JsonConverter(typeof(JsonStringEnumConverter<ModelInput>))]
public enum ModelInput
{
    [JsonStringEnumMemberName("text")] Text,
    [JsonStringEnumMemberName("image")] Image,
}

/// <summary>How reasoning is requested from the server.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ThinkingFormat>))]
public enum ThinkingFormat
{
    [JsonStringEnumMemberName("none")] None,
    /// <summary>OpenAI style <c>reasoning_effort</c>.</summary>
    [JsonStringEnumMemberName("reasoning_effort")] ReasoningEffort,
    /// <summary>Qwen3 style <c>chat_template_kwargs.enable_thinking</c>.</summary>
    [JsonStringEnumMemberName("qwen-chat-template")] QwenChatTemplate,
    /// <summary>Generic <c>chat_template_kwargs</c> passthrough.</summary>
    [JsonStringEnumMemberName("chat-template")] ChatTemplate,
    /// <summary>DeepSeek style <c>thinking: { type: enabled }</c>.</summary>
    [JsonStringEnumMemberName("deepseek")] DeepSeek,
}

/// <summary>Server quirks. Defaults match llama.cpp.</summary>
public sealed record ModelCompat
{
    /// <summary><c>max_tokens</c> or <c>max_completion_tokens</c>.</summary>
    public string MaxTokensField { get; init; } = "max_tokens";
    public bool SupportsFinishReason { get; init; } = true;
    public bool SupportsUsageInStreaming { get; init; } = true;
    public bool SupportsDeveloperRole { get; init; }
    public bool RequiresToolResultName { get; init; }
    public bool RequiresAssistantAfterToolResult { get; init; }
    public bool RequiresThinkingAsText { get; init; }
    public bool RequiresReasoningContentOnAssistantMessages { get; init; }
    public ThinkingFormat ThinkingFormat { get; init; } = ThinkingFormat.None;
    public JsonObject? ChatTemplateKwargs { get; init; }
    /// <summary><c>thinking_budget_tokens</c> (llama.cpp) or <c>thinking_token_budget</c> (vLLM).</summary>
    public string? ThinkingTokenBudgetField { get; init; }
    /// <summary>Null = auto-detect, true/false = force strict JSON-schema tool arguments on or off.</summary>
    public bool? SupportsStrictMode { get; init; }
    /// <summary>
    /// Extra literal phrases and regular expressions (case-insensitive) that mark a provider
    /// error as a context overflow, beyond the built-in set. Matched last, after the parsed
    /// error code and usage counters. Use this when a new endpoint words overflow differently
    /// and shipping a code change would be slower than editing models.json.
    /// </summary>
    public IReadOnlyList<string> OverflowPatterns { get; init; } = [];
    /// <summary>
    /// When true, a named <c>tool_choice</c> also sends llama.cpp/OpenAI
    /// <c>response_format: { type: json_schema }</c>. Defaults to true (llama.cpp).
    /// </summary>
    public bool SupportsJsonSchemaResponseFormat { get; init; } = true;
}

/// <summary>One entry of models.json.</summary>
public sealed record ModelConfig
{
    public required string Id { get; init; }
    public string? Name { get; init; }
    /// <summary>Base URL ending in <c>/v1</c>.</summary>
    public required string BaseUrl { get; init; }
    /// <summary>openai-completions (local/default), openai-codex, or xai.</summary>
    public string Provider { get; init; } = "openai-completions";
    /// <summary>Named subscription connection. Never an API key.</summary>
    public string? Connection { get; init; }
    public string? ApiKey { get; init; }
    /// <summary>Environment variable holding the key. Takes precedence over <see cref="ApiKey"/> when set and present.</summary>
    public string? ApiKeyEnv { get; init; }
    public IReadOnlyList<ModelInput> Input { get; init; } = [ModelInput.Text];
    /// <summary>
    /// The model's context window in tokens, as its server serves it: the broker's for an assigned model, else models.json.
    /// 0 = not configured. No budget is guessed in code (digital-scribe, 2026-10-20: "let's not hardcode anymore stuff in
    /// pi-sharp regarding token budgets... those should come at worst from config files at best from Subscriptions
    /// settings"); until then a model without one silently got 32768.
    /// </summary>
    public int ContextWindow { get; init; }
    /// <summary>
    /// The most the model writes in one answer: the Subscriptions profile's, else models.json. Each request asks for no more
    /// than its window leaves beside its prompt (<c>AgentLoop</c>). 0 = not configured; until 2026-10-20 it silently got 8192.
    /// </summary>
    public int MaxOutputTokens { get; init; }
    /// <summary>
    /// The context size where the seat compacts (down to half of it): an assignment's window from the Subscriptions tab
    /// or a profile's budget (<c>SubscriptionAssignment.WindowIsBudget</c>). Null = as much as <see cref="ContextWindow"/>
    /// holds beside <see cref="MaxOutputTokens"/>.
    /// </summary>
    public int? CompactAtTokens { get; init; }
    /// <summary>Whether the model can reason at all.</summary>
    public bool Reasoning { get; init; }
    public ThinkingLevel DefaultThinkingLevel { get; init; } = ThinkingLevel.Off;
    public ModelCompat Compat { get; init; } = new();
    /// <summary>
    /// Cap on tools advertised to the model. Null means unlimited, except models under
    /// 16k context which default to 8 (Phase 7). Execution still uses the full tool list.
    /// </summary>
    public int? MaxTools { get; init; }

    [JsonIgnore]
    public bool SupportsImages => Input.Contains(ModelInput.Image);

    /// <summary>This model, or an error naming it and the budget that is not configured, prefixed with
    /// <paramref name="source"/> (where the value should have come from).</summary>
    public ModelConfig RequireBudgets(string source)
    {
        if (ContextWindow <= 0)
            throw new InvalidOperationException($"{source}: model '{Id}' has no contextWindow (its context window in tokens, as its server serves it).");
        if (MaxOutputTokens <= 0)
            throw new InvalidOperationException($"{source}: model '{Id}' has no maxOutputTokens (the most it may write in one answer).");
        return this;
    }

    public string? ResolveApiKey()
    {
        if (!string.IsNullOrEmpty(ApiKeyEnv))
        {
            var fromEnv = Environment.GetEnvironmentVariable(ApiKeyEnv);
            if (!string.IsNullOrEmpty(fromEnv)) return fromEnv;
        }
        return ApiKey;
    }
}
