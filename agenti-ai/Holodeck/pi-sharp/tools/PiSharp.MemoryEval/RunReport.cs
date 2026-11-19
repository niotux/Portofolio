using System.Text.Json;

namespace PiSharp.MemoryEval;

/// <summary>Outcome of a single case in a run: <c>pass</c> (all checks green), <c>fail</c> (a
/// deterministic check failed: privacy leak, forbidden phrase, retrieval mismatch) or
/// <c>scored</c> (no deterministic failure; judge score carried).</summary>
public enum EvalStatus { Scored, Pass, Fail }

/// <summary>Per-case result in a run report.</summary>
public sealed class EvalCaseResult
{
    public required string CaseId { get; init; }
    public required EvalStatus Status { get; set; }
    /// <summary>Judge score in [0,1]; null when the case was decided deterministically.</summary>
    public double? Score { get; init; }
    /// <summary>Deterministic failures that forced <see cref="EvalStatus.Fail"/> (leaks, forbidden phrases).</summary>
    public List<string> Failures { get; set; } = [];
    /// <summary>Human-review evidence: concepts covered / not covered.</summary>
    public List<string> CoveredConcepts { get; init; } = [];
    public List<string> MissingConcepts { get; init; } = [];
}

/// <summary>
/// The outcome of one suite run. Serializes to <c>reports/<suite>.json</c>. A suite passes when
/// the mean score over scored cases is >= the suite threshold AND no case carries a
/// deterministic failure. A single privacy/isolation leak fails the whole suite regardless of
/// how well the other cases score - the threshold is for quality, not for the hard checks.
/// </summary>
public sealed class RunReport
{
    public required string SuiteId { get; init; }
    public required string SuiteVersion { get; set; }
    public required string JudgeProfile { get; init; }
    public required string Mode { get; init; }   // "recorded" | "live"
    public required string RunAt { get; init; }  // ISO 8601 UTC
    public required List<EvalCaseResult> Cases { get; init; }

    public bool AnyFailure => Cases.Any(c => c.Status == EvalStatus.Fail || c.Failures.Count > 0);

    /// <summary>Mean score over the cases the judge scored (deterministic-only runs have none).</summary>
    public double MeanScore
    {
        get
        {
            var scores = Cases.Where(c => c.Score is { } s).Select(c => c.Score!.Value).ToList();
            return scores.Count == 0 ? 0 : scores.Average();
        }
    }

    public bool AboveThreshold(double passThreshold) => MeanScore >= passThreshold;

    public bool Passed(double passThreshold) => !AnyFailure && AboveThreshold(passThreshold);

    public string ToJson() => JsonSerializer.Serialize(this, MemoryEvalJsonContext.Default.RunReport);

    public static RunReport FromJson(string json) =>
        (RunReport?)JsonSerializer.Deserialize(json, MemoryEvalJsonContext.Default.RunReport)
        ?? throw new InvalidOperationException("Could not deserialize run report.");
}

/// <summary>
/// The last-approved (operator-blessed) outcome per case for a suite, keyed by suiteVersion.
/// Lives at <c>Evals/baselines/<suiteId>.json</c> and is updated only by an explicit
/// <c>--update-baseline</c> run (ai-blue's gate rule: baselines are a human decision, not a
/// side effect of a test run).
/// </summary>
public sealed class Baseline
{
    public required string SuiteId { get; init; }
    public required string SuiteVersion { get; init; }
    public required string BlessedAt { get; init; }
    public Dictionary<string, double> CaseScores { get; init; } = [];

    public string ToJson() => JsonSerializer.Serialize(this, MemoryEvalJsonContext.Default.Baseline);

    public static Baseline FromJson(string json) =>
        (Baseline?)JsonSerializer.Deserialize(json, MemoryEvalJsonContext.Default.Baseline)
        ?? throw new InvalidOperationException("Could not deserialize baseline.");

    /// <summary>Builds a baseline from a passing run (the operator-blessed snapshot).</summary>
    public static Baseline FromRun(RunReport report) => new()
    {
        SuiteId = report.SuiteId,
        SuiteVersion = report.SuiteVersion,
        BlessedAt = report.RunAt,
        CaseScores = report.Cases
            .Where(c => c.Score is { } s)
            .ToDictionary(c => c.CaseId, c => c.Score!.Value)
    };
}

/// <summary>
/// The baseline-diff result: per-case delta (current - blessed) and the gate verdict. The diff
/// is what the CI gate posts - a regression beyond the tolerance on any case, a suite version
/// mismatch, or a new deterministic failure fails the gate.
/// </summary>
public sealed class BaselineDiff
{
    public required string SuiteId { get; init; }
    public bool VersionMatch { get; init; }
    public List<CaseDelta> Deltas { get; init; } = [];

    /// <summary>Worst (most negative) score delta; 0 when no scored case regressed.</summary>
    public double MinDelta => Deltas.Count == 0 ? 0 : Deltas.Min(d => d.Delta!.Value);

    /// <summary>Gate: fails on version mismatch, any failed case, or a delta below -tolerance.</summary>
    public bool GatePasses(RunReport current, double tolerance) =>
        VersionMatch && !current.AnyFailure && MinDelta >= -tolerance;

    public string ToJson() => JsonSerializer.Serialize(this, MemoryEvalJsonContext.Default.BaselineDiff);

    public static BaselineDiff Compute(Baseline baseline, RunReport current)
    {
        var deltas = new List<CaseDelta>();
        foreach (var c in current.Cases)
        {
            if (c.Score is not { } score) continue;
            double? blessed = baseline.CaseScores.TryGetValue(c.CaseId, out var b) ? b : null;
            deltas.Add(new CaseDelta(c.CaseId, blessed, score, blessed is null ? null : score - blessed.Value));
        }
        return new BaselineDiff
        {
            SuiteId = current.SuiteId,
            VersionMatch = baseline.SuiteVersion == current.SuiteVersion,
            Deltas = deltas,
        };
    }

    public sealed record CaseDelta(string CaseId, double? Blessed, double Current, double? Delta);
}
