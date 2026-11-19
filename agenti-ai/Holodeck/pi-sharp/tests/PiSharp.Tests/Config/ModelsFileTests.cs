using PiSharp.Core.Config;
using PiSharp.Core.Models;
using PiSharp.Tests.Tools;

namespace PiSharp.Tests.Configuration;

public class ModelsFileTests
{
    [Fact]
    public void ParseWrappedArrayAndCompat()
    {
        var models = ModelsFile.Parse("""
            {
              "models": [
                {
                  "id": "qwen3",
                  "baseUrl": "http://localhost:8080/v1/",
                  "apiKeyEnv": "LLAMA_API_KEY",
                  "input": ["text", "image"],
                  "contextWindow": 65536,
                  "maxOutputTokens": 4096,
                  "compat": { "maxTokensField": "max_tokens", "thinkingFormat": "qwen-chat-template" },
                  "reasoning": { "default": "off" }
                }
              ]
            }
            """);
        var m = Assert.Single(models);
        Assert.Equal("qwen3", m.Id);
        Assert.Equal("http://localhost:8080/v1", m.BaseUrl);
        Assert.True(m.SupportsImages);
        Assert.Equal(ThinkingFormat.QwenChatTemplate, m.Compat.ThinkingFormat);
        Assert.Equal(ThinkingLevel.Off, m.DefaultThinkingLevel);
        Assert.Equal(65536, m.ContextWindow);
    }

    [Fact]
    public void ParsesExtensibleOverflowPatterns()
    {
        // P1 (wi-8): a provider that words overflow in prose no built-in pattern covers can be
        // taught via models.json, so recognition is not frozen to a hardcoded regex list.
        var models = ModelsFile.Parse("""
            {
              "models": [
                {
                  "id": "oddbox",
                  "baseUrl": "http://localhost:1/v1",
                  "contextWindow": 32768,
                  "maxOutputTokens": 4096,
                  "compat": { "overflowPatterns": ["slot budget blown", "please trim\\s+your\\s+prompt"] }
                }
              ]
            }
            """);
        var m = Assert.Single(models);
        Assert.Equal(2, m.Compat.OverflowPatterns.Count);
        Assert.Equal("slot budget blown", m.Compat.OverflowPatterns[0]);
    }

    [Fact]
    public void ProjectOverridesById()
    {
        using var tmp = new TempWorkspace();
        var global = tmp.Dir("global");
        var cwd = tmp.Dir("proj");
        Directory.CreateDirectory(Path.Combine(cwd, ".pi-sharp"));
        File.WriteAllText(Path.Combine(global, "models.json"), """{"models":[{"id":"a","baseUrl":"http://g/v1","contextWindow":32768,"maxOutputTokens":4096},{"id":"b","baseUrl":"http://g/v1","contextWindow":32768,"maxOutputTokens":4096}]}""");
        File.WriteAllText(Path.Combine(cwd, ".pi-sharp", "models.json"), """{"models":[{"id":"a","baseUrl":"http://p/v1","contextWindow":32768,"maxOutputTokens":4096}]}""");

        var models = ModelsFile.Load(cwd, global);
        Assert.Equal(2, models.Count);
        Assert.Equal("http://p/v1", ModelsFile.Find(models, "a")!.BaseUrl);
        Assert.Equal("http://g/v1", ModelsFile.Find(models, "b")!.BaseUrl);
    }

    [Fact]
    public void FindMatchesPrefixWhenUnique()
    {
        var models = ModelsFile.Parse("""[{"id":"maxiclaude","baseUrl":"http://x/v1","contextWindow":32768,"maxOutputTokens":4096},{"id":"other","baseUrl":"http://x/v1","contextWindow":32768,"maxOutputTokens":4096}]""");
        Assert.Equal("maxiclaude", ModelsFile.Find(models, "maxi")!.Id);
        Assert.Null(ModelsFile.Find(models, "nope"));
    }

    /// <summary>digital-scribe, 2026-10-20: "let's not hardcode anymore stuff in pi-sharp regarding token budgets... those
    /// should come at worst from config files at best from Subscriptions settings". A models.json entry without its window
    /// or output is a load error naming the model and the field; it was 32768/8192 before.</summary>
    [Theory]
    [InlineData("""{"id":"qwen3","baseUrl":"http://x/v1","maxOutputTokens":4096}""", "contextWindow")]
    [InlineData("""{"id":"qwen3","baseUrl":"http://x/v1","contextWindow":65536}""", "maxOutputTokens")]
    [InlineData("""{"id":"qwen3","baseUrl":"http://x/v1","contextWindow":0,"maxOutputTokens":4096}""", "contextWindow")]
    public void AModelWithoutItsWindowOrOutputIsALoadErrorNamingBoth(string entry, string field)
    {
        var error = Assert.Throws<InvalidOperationException>(() => ModelsFile.Parse("[" + entry + "]"));
        Assert.Contains("'qwen3'", error.Message);
        Assert.Contains(field, error.Message);

        using var tmp = new TempWorkspace();
        var global = tmp.Dir("global");
        File.WriteAllText(Path.Combine(global, "models.json"), "[" + entry + "]");
        var loaded = Assert.Throws<InvalidOperationException>(() => ModelsFile.Load(tmp.Dir("proj"), global));
        Assert.Contains(Path.Combine(global, "models.json"), loaded.Message);
        Assert.Contains(field, loaded.Message);
    }

    [Fact]
    public void TheEnvironmentModelNeedsItsWindowAndOutputFromTheEnvironment()
    {
        var env = new Dictionary<string, string> { ["PI_SHARP_BASE_URL"] = "http://127.0.0.1:8080", ["PI_SHARP_MODEL"] = "qwen3" };
        string? Get(string name) => env.TryGetValue(name, out var value) ? value : null;

        var missing = Assert.Throws<InvalidOperationException>(() => ModelsFile.FromEnvironment(Get));
        Assert.Contains("PI_SHARP_CONTEXT_WINDOW", missing.Message);
        Assert.Contains("'qwen3'", missing.Message);

        env["PI_SHARP_CONTEXT_WINDOW"] = "65536";
        Assert.Contains("PI_SHARP_MAX_OUTPUT_TOKENS", Assert.Throws<InvalidOperationException>(() => ModelsFile.FromEnvironment(Get)).Message);

        env["PI_SHARP_MAX_OUTPUT_TOKENS"] = "4096";
        var model = ModelsFile.FromEnvironment(Get)!;
        Assert.Equal(65536, model.ContextWindow);
        Assert.Equal(4096, model.MaxOutputTokens);
        Assert.Equal("http://127.0.0.1:8080/v1", model.BaseUrl);

        Assert.Null(ModelsFile.FromEnvironment(_ => null)); // nothing named: no environment model at all
    }
}
