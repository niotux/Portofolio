using System.Text.Json;
using PiSharp.Core.Agent;
using PiSharp.Core.Models;

namespace PiSharp.MemoryEval;

/// <summary>
/// A named judge profile: the model and sampling settings the judge (and, in live mode, the
/// model under test) runs with. Profiles live under <c>Profiles/*.json</c> and are referenced by
/// name from a suite (<c>judgeProfile</c>) - never inlined into the suite - so a suite can be
/// re-judged against a new profile without a suite-version bump.
/// </summary>
public sealed class JudgeProfile
{
    /// <summary>Profile name, e.g. <c>miniclaude_judge</c>. Suites reference this.</summary>
    public required string Name { get; init; }

    /// <summary>OpenAI-compatible base URL, e.g. <c>http://192.168.1.101:8083/v1</c>.</summary>
    public required string BaseUrl { get; init; }

    /// <summary>Model id served at the base URL.</summary>
    public required string Model { get; init; }

    /// <summary>Sampling temperature. Judge profiles run low (ai-blue holds canon recall at 0.15).</summary>
    public double Temperature { get; init; } = 0.1;

    /// <summary>Thinking level; judges run it off (deterministic scoring, no hidden reasoning).</summary>
    public Core.Models.ThinkingLevel Thinking { get; init; } = Core.Models.ThinkingLevel.Off;

    /// <summary>Max output tokens for the judge call.</summary>
    public int MaxOutputTokens { get; init; } = 1024;

    /// <summary>The model's context window in tokens, as its server serves it. No default (digital-scribe, 2026-10-20: no
    /// token budgets hard-coded in pi-sharp): a profile without it runs no agent turn, the loop names the missing field.</summary>
    public int ContextWindow { get; init; }

    /// <summary>API key override; empty means use the model default (llama.cpp needs none).</summary>
    public string? ApiKey { get; init; }

    public static JudgeProfile Load(string path) =>
        (JudgeProfile?)JsonSerializer.Deserialize(File.ReadAllText(path), MemoryEvalJsonContext.Default.JudgeProfile)
        ?? throw new InvalidOperationException($"Empty profile file: {path}");

    /// <summary>Loads a profile from the bundled <c>Profiles/</c> directory by name.</summary>
    public static JudgeProfile LoadBundled(string name)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Profiles");
        return Load(Path.Combine(dir, name + ".json"));
    }

    /// <summary>A <see cref="Core.Models.ModelConfig"/> for the underlying LLM client (sampling settings come from <see cref="ToStreamOptions"/>).</summary>
    public Core.Models.ModelConfig ToModelConfig() => new()
    {
        Id = Model,
        Name = Name,
        BaseUrl = BaseUrl,
        ApiKey = ApiKey,
        ContextWindow = ContextWindow,
        MaxOutputTokens = MaxOutputTokens,
    };

    /// <summary>Sampling settings for a call with this profile (temperature, thinking off).</summary>
    public StreamOptions ToStreamOptions() => new()
    {
        Temperature = Temperature,
        Reasoning = Thinking,
        MaxTokens = MaxOutputTokens,
    };
}
