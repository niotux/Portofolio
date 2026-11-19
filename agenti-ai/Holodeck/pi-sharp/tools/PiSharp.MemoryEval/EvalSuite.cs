using System.Text.Json;
using System.Text.Json.Serialization;

namespace PiSharp.MemoryEval;

/// <summary>
/// One versioned eval case. Suites live under <c>Evals/*.json</c> and carry a <c>suiteVersion</c>;
/// the baseline is keyed by <c>suiteVersion</c> + <c>caseId</c> so a suite change forces a fresh,
/// operator-blessed baseline rather than silently comparing against a stale one.
/// </summary>
public sealed class EvalCase
{
    /// <summary>Stable case id (kebab-case). The baseline and the report key on this.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>Which pipeline dimension this case checks: recall | non-fabrication | privacy | isolation | voice.</summary>
    [JsonPropertyName("dimension")]
    public required string Dimension { get; init; }

    /// <summary>The composed prompt presented to the model under test (the seat's memory context is inlined).</summary>
    [JsonPropertyName("prompt")]
    public required string Prompt { get; init; }

    /// <summary>Provenance pointer to the real turn or work record from which this case was drawn.</summary>
    [JsonPropertyName("source")]
    public string? Source { get; init; }

    /// <summary>Memory entries (id + text) that the retrieval leg is expected to surface for this case.</summary>
    [JsonPropertyName("expectedMemoryIds")]
    public List<string> ExpectedMemoryIds { get; init; } = [];

    /// <summary>Memory ids the case must NOT surface (privacy / persona isolation). A leak fails the case outright.</summary>
    [JsonPropertyName("forbiddenMemoryIds")]
    public List<string> ForbiddenMemoryIds { get; init; } = [];

    /// <summary>Concepts the response should cover (human-review evidence, not a hard gate).</summary>
    [JsonPropertyName("requiredConcepts")]
    public List<string> RequiredConcepts { get; init; } = [];

    /// <summary>Phrases that must not appear. A literal match is a deterministic failure (non-fabrication / privacy).</summary>
    [JsonPropertyName("forbiddenPhrases")]
    public List<string> ForbiddenPhrases { get; init; } = [];

    /// <summary>
    /// Committed response for recorded mode. When present, recorded mode judges this fixture
    /// instead of calling a model; this is how the suite runs in CI with no live model.
    /// </summary>
    [JsonPropertyName("recordedResponse")]
    public string? RecordedResponse { get; init; }
}

/// <summary>A versioned eval suite: a set of cases plus the pass threshold for the suite.</summary>
public sealed class EvalSuite
{
    [JsonPropertyName("suiteId")]
    public required string SuiteId { get; init; }

    /// <summary>Semver of the suite contents. Changing it invalidates the committed baseline for it.</summary>
    [JsonPropertyName("suiteVersion")]
    public required string SuiteVersion { get; init; }

    /// <summary>
    /// The judge profile this suite scores against, e.g. <c>miniclaude_judge</c>. The profile
    /// (temperature, thinking, max output) is resolved from <c>Profiles/*.json</c>, never inlined.
    /// </summary>
    [JsonPropertyName("judgeProfile")]
    public required string JudgeProfile { get; init; }

    /// <summary>
    /// Pass threshold in [0,1] over the suite's case scores: the suite passes when the mean case
    /// score is >= this AND no case failed a deterministic check (a privacy/isolation leak, a
    /// forbidden phrase, a retrieval-count mismatch). Style quality cannot compensate for a leak.
    /// </summary>
    [JsonPropertyName("passThreshold")]
    public double PassThreshold { get; init; } = 0.8;

    [JsonPropertyName("cases")]
    public List<EvalCase> Cases { get; init; } = [];

    public static EvalSuite Load(string path) =>
        (EvalSuite?)JsonSerializer.Deserialize(File.ReadAllText(path), MemoryEvalJsonContext.Default.EvalSuite)
        ?? throw new InvalidOperationException($"Empty suite file: {path}");
}

