using PiSharp.MemoryEval;
using Xunit;

namespace PiSharp.Tests.MemoryEval;

/// <summary>
/// Recorded-mode tests for the memory eval harness (scaffold, pi-sharp-wi-memory-phase0).
/// Zero live calls: recorded mode judges committed fixtures, so these run in CI with no model.
/// </summary>
public class MemoryEvalHarnessTests
{
    private static EvalCase Case(string id, string response,
        List<string>? expected = null, List<string>? forbiddenIds = null,
        List<string>? forbiddenPhrases = null, List<string>? concepts = null) => new()
    {
        Id = id,
        Dimension = "recall",
        Prompt = "p",
        RecordedResponse = response,
        ExpectedMemoryIds = expected ?? [],
        ForbiddenMemoryIds = forbiddenIds ?? [],
        ForbiddenPhrases = forbiddenPhrases ?? [],
        RequiredConcepts = concepts ?? [],
    };

    [Fact]
    public void RecordedRun_PassesOnCleanFixture()
    {
        var suite = new EvalSuite
        {
            SuiteId = "s", SuiteVersion = "0.1.0", JudgeProfile = "p",
            Cases = [Case("c1", "uses xunit per mem-0001", expected: ["mem-0001"], concepts: ["xunit"])],
        };
        var report = new Judge().RunRecorded(suite);
        Assert.False(report.AnyFailure);
        Assert.True(report.Passed(suite.PassThreshold));
        Assert.Equal(EvalStatus.Pass, report.Cases[0].Status);
    }

    [Fact]
    public void RecordedRun_FailsOnForbiddenPhrase()
    {
        var suite = new EvalSuite
        {
            SuiteId = "s", SuiteVersion = "0.1.0", JudgeProfile = "p",
            Cases = [Case("c1", "the host is localhost:5432", forbiddenPhrases: ["localhost:5432"])],
        };
        var report = new Judge().RunRecorded(suite);
        Assert.True(report.AnyFailure);
        Assert.Equal(EvalStatus.Fail, report.Cases[0].Status);
        Assert.False(report.Passed(0.8));
        Assert.Contains(report.Cases[0].Failures, f => f.Contains("forbidden phrase"));
    }

    [Fact]
    public void RecordedRun_FailsOnPrivacyLeak()
    {
        // A foreign-seat memory id surfacing is a privacy/isolation failure even though the
        // response otherwise looks fine.
        var suite = new EvalSuite
        {
            SuiteId = "s", SuiteVersion = "0.1.0", JudgeProfile = "p",
            Cases = [Case("c1", "note mem-9999 says x", forbiddenIds: ["mem-9999"], concepts: ["x"])],
        };
        var report = new Judge().RunRecorded(suite);
        Assert.True(report.AnyFailure);
        Assert.Contains(report.Cases[0].Failures, f => f.Contains("mem-9999"));
    }

    [Fact]
    public void RecordedRun_FailsWhenExpectedMemoryNotSurfaced()
    {
        var suite = new EvalSuite
        {
            SuiteId = "s", SuiteVersion = "0.1.0", JudgeProfile = "p",
            Cases = [Case("c1", "it uses xunit", expected: ["mem-0001"], concepts: ["xunit"])],
        };
        var report = new Judge().RunRecorded(suite);
        Assert.Contains(report.Cases[0].Failures, f => f.Contains("mem-0001"));
    }

    [Fact]
    public void RecordedRun_MissingRecordedResponseThrows()
    {
        var suite = new EvalSuite
        {
            SuiteId = "s", SuiteVersion = "0.1.0", JudgeProfile = "p",
            Cases = [new EvalCase { Id = "c1", Dimension = "recall", Prompt = "p" }],
        };
        Assert.Throws<InvalidOperationException>(() => new Judge().RunRecorded(suite));
    }

    [Fact]
    public void ThresholdMath_MeanScoreAndGate()
    {
        // Two scored cases at 1.0 and 0.5 -> mean 0.75; below a 0.8 threshold the gate fails
        // even though no deterministic check fired. Multi-char concepts keep substring matching
        // honest (ContainsConcept matches on substrings, so single letters would false-positive).
        var suite = new EvalSuite
        {
            SuiteId = "s", SuiteVersion = "0.1.0", JudgeProfile = "p", PassThreshold = 0.8,
            Cases =
            [
                Case("c1", "the notes mention alpha and beta and gamma",
                    concepts: ["alpha", "beta", "gamma"]),                                          // 1.0
                Case("c2", "the notes mention alpha and beta only",
                    concepts: ["alpha", "beta", "gamma", "delta"]),                                  // 0.5
            ],
        };
        var report = new Judge().RunRecorded(suite);
        Assert.False(report.AnyFailure);
        Assert.False(report.AboveThreshold(0.8));   // mean 0.75 < 0.8
        Assert.True(report.AboveThreshold(0.7));
    }

    [Fact]
    public void BaselineDiff_DetectsRegressionAndVersionMismatch()
    {
        var blessed = new Baseline
        {
            SuiteId = "s", SuiteVersion = "1.0.0", BlessedAt = "t",
            CaseScores = new() { ["c1"] = 1.0, ["c2"] = 1.0 },
        };
        var current = new RunReport
        {
            SuiteId = "s", SuiteVersion = "1.0.0", JudgeProfile = "p", Mode = "recorded", RunAt = "t",
            Cases =
            [
                new EvalCaseResult { CaseId = "c1", Status = EvalStatus.Pass, Score = 1.0 },
                new EvalCaseResult { CaseId = "c2", Status = EvalStatus.Scored, Score = 0.4 },
            ],
        };
        var diff = BaselineDiff.Compute(blessed, current);
        Assert.True(diff.VersionMatch);
        Assert.False(diff.GatePasses(current, 0.1));          // c2 delta -0.6 < -0.1
        Assert.Equal(-0.6, diff.MinDelta, 9);

        // A suite version change breaks the gate even with identical scores (round-trip gives a copy).
        var bumped = RunReport.FromJson(current.ToJson());
        bumped.SuiteVersion = "2.0.0";
        Assert.False(BaselineDiff.Compute(blessed, bumped).GatePasses(bumped, 0.1));
    }

    [Fact]
    public void BundledExampleSuite_LoadsAndPassesRecorded()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Evals", "memory-recall.json");
        var suite = EvalSuite.Load(path);
        var profile = JudgeProfile.LoadBundled(suite.JudgeProfile);
        var report = new Judge().RunRecorded(suite);
        Assert.Equal("miniclaude_judge", profile.Name);
        Assert.Equal("miniclaude", profile.Model);
        Assert.Equal(0.1, profile.Temperature);
        Assert.Equal(12, suite.Cases.Count);
        Assert.All(suite.Cases, c => Assert.False(string.IsNullOrWhiteSpace(c.Source), $"{c.Id} needs source provenance"));
        Assert.False(report.AnyFailure);
        Assert.True(report.Passed(suite.PassThreshold));
    }
}
