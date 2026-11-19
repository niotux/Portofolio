// models.json loader. Replaces pi's 40-provider catalog (docs/02-library-map.md).
using System.Text.Json.Nodes;
using PiSharp.Core.Models;

namespace PiSharp.Core.Config;

/// <summary>Loads <c>models.json</c> from global and project config dirs. Project entries override by id.</summary>
public static class ModelsFile
{
    public static IReadOnlyList<ModelConfig> Load(string cwd, string? globalDir = null)
    {
        var byId = new Dictionary<string, ModelConfig>(StringComparer.OrdinalIgnoreCase);
        AddAll(byId, ParseFile(ConfigPaths.ModelsFile(ConfigPaths.GlobalConfigDir(globalDir))));
        AddAll(byId, ParseFile(ConfigPaths.ModelsFile(ConfigPaths.ProjectConfigDir(cwd))));
        return byId.Values.ToList();
    }

    public static IReadOnlyList<ModelConfig> ParseFile(string path)
    {
        if (!File.Exists(path)) return [];
        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return [];
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException($"{path}: {ex.Message}", ex);
        }
    }

    public static IReadOnlyList<ModelConfig> Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        JsonNode? node;
        try { node = JsonNode.Parse(json); }
        catch (System.Text.Json.JsonException) { return []; }

        JsonArray? array = node switch
        {
            JsonArray a => a,
            JsonObject obj when obj["models"] is JsonArray models => models,
            JsonObject obj => new JsonArray(obj),
            _ => null,
        };
        if (array is null) return [];

        var list = new List<ModelConfig>();
        foreach (var item in array)
        {
            if (item is JsonObject model && TryParseModel(model, out var config))
                list.Add(config);
        }
        return list;
    }

    public static ModelConfig? Find(IReadOnlyList<ModelConfig> models, string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || models.Count == 0) return null;
        var exact = models.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;
        var named = models.FirstOrDefault(m => string.Equals(m.Name, id, StringComparison.OrdinalIgnoreCase));
        if (named is not null) return named;
        var prefix = models.Where(m => m.Id.StartsWith(id, StringComparison.OrdinalIgnoreCase)
                                       || (m.Name is not null && m.Name.StartsWith(id, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        return prefix.Count == 1 ? prefix[0] : null;
    }

    /// <summary>
    /// Last-resort model from env vars so a sandbox can run without a models.json.
    /// <c>PI_SHARP_BASE_URL</c> / <c>PI_SHARP_API_KEY</c> / <c>PI_SHARP_MODEL</c>, falling back to the live-test vars, and
    /// its budgets <c>PI_SHARP_CONTEXT_WINDOW</c> / <c>PI_SHARP_MAX_OUTPUT_TOKENS</c>, which have no default: a model named
    /// without them is an error, not a 32768/8192 guess (digital-scribe, 2026-10-20: "let's not hardcode anymore stuff in
    /// pi-sharp regarding token budgets").
    /// </summary>
    public static ModelConfig? FromEnvironment() => FromEnvironment(Environment.GetEnvironmentVariable);

    /// <summary><see cref="FromEnvironment()"/> over <paramref name="env"/>, so a test needs no process environment.</summary>
    public static ModelConfig? FromEnvironment(Func<string, string?> env)
    {
        var baseUrl = FirstEnv(env, "PI_SHARP_BASE_URL", "PI_SHARP_TEST_BASE_URL");
        var model = FirstEnv(env, "PI_SHARP_MODEL", "PI_SHARP_TEST_MODEL");
        if (string.IsNullOrWhiteSpace(baseUrl) && string.IsNullOrWhiteSpace(model)) return null;
        baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? "http://127.0.0.1:8080/v1" : baseUrl.TrimEnd('/');
        if (!baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            baseUrl = baseUrl.TrimEnd('/') + "/v1";
        var apiKey = FirstEnv(env, "PI_SHARP_API_KEY", "PI_SHARP_TEST_API_KEY") ?? "dummy";
        var id = string.IsNullOrWhiteSpace(model) ? "default" : model;
        return new ModelConfig
        {
            Id = id,
            Name = model,
            BaseUrl = baseUrl,
            ApiKey = apiKey,
            Input = [ModelInput.Text, ModelInput.Image],
            ContextWindow = EnvTokens(env, "PI_SHARP_CONTEXT_WINDOW", id, "context window"),
            MaxOutputTokens = EnvTokens(env, "PI_SHARP_MAX_OUTPUT_TOKENS", id, "most it may write in one answer"),
            Reasoning = true,
            DefaultThinkingLevel = ThinkingLevel.Off,
            Compat = new ModelCompat { ThinkingFormat = ThinkingFormat.QwenChatTemplate },
        };
    }

    private static int EnvTokens(Func<string, string?> env, string name, string model, string what) =>
        int.TryParse(env(name), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) && n > 0
            ? n
            : throw new InvalidOperationException($"environment model '{model}': {name} is not set to a positive number of tokens (its {what}).");

    private static void AddAll(Dictionary<string, ModelConfig> byId, IReadOnlyList<ModelConfig> models)
    {
        foreach (var model in models) byId[model.Id] = model;
    }

    private static bool TryParseModel(JsonObject obj, out ModelConfig config)
    {
        config = null!;
        var id = AsString(obj["id"]);
        var baseUrl = AsString(obj["baseUrl"]);
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(baseUrl)) return false;

        var input = ParseInput(obj["input"]);
        var (reasoning, defaultLevel) = ParseReasoning(obj["reasoning"], obj["defaultThinkingLevel"]);
        config = new ModelConfig
        {
            Id = id,
            Name = AsString(obj["name"]),
            BaseUrl = baseUrl.TrimEnd('/'),
            Provider = AsString(obj["provider"]) ?? "openai-completions",
            Connection = AsString(obj["connection"]),
            ApiKey = AsString(obj["apiKey"]),
            ApiKeyEnv = AsString(obj["apiKeyEnv"]),
            Input = input,
            ContextWindow = RequiredTokens(obj, "contextWindow", id),
            MaxOutputTokens = RequiredTokens(obj, "maxOutputTokens", id),
            Reasoning = reasoning,
            DefaultThinkingLevel = defaultLevel,
            Compat = ParseCompat(obj["compat"] as JsonObject),
        };
        return true;
    }

    /// <summary>A budget every models.json entry must state (digital-scribe, 2026-10-20: token budgets come "at worst from
    /// config files at best from Subscriptions settings"): missing or not positive is a load error naming the model and the
    /// field, where it was 32768/8192 before.</summary>
    private static int RequiredTokens(JsonObject obj, string field, string id) =>
        AsInt(obj[field]) is int n && n > 0
            ? n
            : throw new InvalidOperationException($"model '{id}' has no {field}: every models.json entry states its contextWindow and maxOutputTokens in tokens.");

    private static IReadOnlyList<ModelInput> ParseInput(JsonNode? node)
    {
        if (node is JsonArray array)
        {
            var list = new List<ModelInput>();
            foreach (var item in array)
            {
                var s = AsString(item);
                if (string.Equals(s, "image", StringComparison.OrdinalIgnoreCase)) list.Add(ModelInput.Image);
                else if (string.Equals(s, "text", StringComparison.OrdinalIgnoreCase)) list.Add(ModelInput.Text);
            }
            if (list.Count > 0) return list;
        }
        return [ModelInput.Text];
    }

    private static (bool Reasoning, ThinkingLevel Level) ParseReasoning(JsonNode? reasoning, JsonNode? defaultLevelNode)
    {
        var level = ParseThinking(AsString(defaultLevelNode)) ?? ThinkingLevel.Off;
        if (reasoning is JsonValue v)
        {
            if (v.TryGetValue<bool>(out var flag)) return (flag, level);
            if (v.TryGetValue<string>(out var s) && ParseThinking(s) is { } parsed) return (parsed != ThinkingLevel.Off, parsed);
        }
        if (reasoning is JsonObject obj)
        {
            var nested = ParseThinking(AsString(obj["default"])) ?? level;
            return (nested != ThinkingLevel.Off || AsBool(obj["enabled"]) == true, nested);
        }
        return (false, level);
    }

    private static ModelCompat ParseCompat(JsonObject? obj)
    {
        if (obj is null) return new ModelCompat();
        return new ModelCompat
        {
            MaxTokensField = AsString(obj["maxTokensField"]) ?? "max_tokens",
            SupportsFinishReason = AsBool(obj["supportsFinishReason"]) ?? true,
            SupportsUsageInStreaming = AsBool(obj["supportsUsageInStreaming"]) ?? true,
            SupportsDeveloperRole = AsBool(obj["supportsDeveloperRole"]) ?? false,
            RequiresToolResultName = AsBool(obj["requiresToolResultName"]) ?? false,
            RequiresAssistantAfterToolResult = AsBool(obj["requiresAssistantAfterToolResult"]) ?? false,
            RequiresThinkingAsText = AsBool(obj["requiresThinkingAsText"]) ?? false,
            RequiresReasoningContentOnAssistantMessages = AsBool(obj["requiresReasoningContentOnAssistantMessages"]) ?? false,
            ThinkingFormat = ParseThinkingFormat(AsString(obj["thinkingFormat"])),
            ChatTemplateKwargs = obj["chatTemplateKwargs"] as JsonObject,
            ThinkingTokenBudgetField = AsString(obj["thinkingTokenBudgetField"]),
            SupportsStrictMode = AsBool(obj["supportsStrictMode"]),
            OverflowPatterns = ParseStringList(obj["overflowPatterns"]),
        };
    }

    private static IReadOnlyList<string> ParseStringList(JsonNode? node) =>
        node is JsonArray array
            ? array.OfType<JsonValue>().Select(v => v.GetValue<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList()
            : [];

    private static ThinkingFormat ParseThinkingFormat(string? raw) => raw switch
    {
        "reasoning_effort" => ThinkingFormat.ReasoningEffort,
        "qwen-chat-template" => ThinkingFormat.QwenChatTemplate,
        "chat-template" => ThinkingFormat.ChatTemplate,
        "deepseek" => ThinkingFormat.DeepSeek,
        _ => ThinkingFormat.None,
    };

    private static ThinkingLevel? ParseThinking(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return Enum.TryParse<ThinkingLevel>(raw.Replace("-", ""), ignoreCase: true, out var level) ? level : null;
    }

    private static string? AsString(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static bool? AsBool(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;

    private static int? AsInt(JsonNode? node)
    {
        if (node is not JsonValue v) return null;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<double>(out var d)) return (int)d;
        return null;
    }

    private static string? FirstEnv(Func<string, string?> env, params string[] names)
    {
        foreach (var name in names)
        {
            var value = env(name);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return null;
    }
}
