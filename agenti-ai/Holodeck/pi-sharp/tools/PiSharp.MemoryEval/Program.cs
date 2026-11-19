using PiSharp.MemoryEval;

// pi-sharp memory eval harness (adr-pi-sharp-memory-pipeline, scaffold - pi-sharp-wi-memory-phase0).
//
//   pi-sharp-memory-eval run <suite.json> [--mode recorded|live] [--profile <name>]
//   pi-sharp-memory-eval diff <suite.json> --baseline <baseline.json> [--tolerance 0.1]
//   pi-sharp-memory-eval update-baseline <suite.json> --from <report.json> --baseline <baseline.json>
//
// recorded mode is the CI gate: deterministic checks + concept coverage on committed fixtures,
// no model, no network. live mode judges against a real model (opt-in). The baseline is a
// human-blessed snapshot (ai-blue's rule) updated only by update-baseline.

var mode = ParseFlag(args, "--mode", "recorded");

if (FirstCommand(args) == "pipeline")
{
    var directory = ParseFlag(args, "--output-dir", null) ?? throw new UsageException("pipeline requires --output-dir <fresh directory>");
    var profile = JudgeProfile.LoadBundled(ParseFlag(args, "--profile", null) ?? throw new UsageException("pipeline requires an explicitly selected --profile"));
    var client = new PiSharp.Llm.OpenAi.OpenAiCompletionsClient();
    var embeddingUrl = ParseFlag(args, "--embedding-url", null);
    var embeddingModel = ParseFlag(args, "--embedding-model", null);
    if ((embeddingUrl is null) != (embeddingModel is null)) throw new UsageException("Pass both --embedding-url and --embedding-model.");
    using var embeddings = embeddingUrl is not null && embeddingModel is not null
        ? new PiSharp.Llm.NotebookEmbeddingsClient(embeddingUrl, embeddingModel,
            ParseFlag(args, "--embedding-api-key-env", null) is { } keyEnv ? Environment.GetEnvironmentVariable(keyEnv) : null) : null;
    using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(20));
    var report = await PipelineEvaluation.RunAsync(directory, profile.ToModelConfig(), client.Stream, embeddings, deadline.Token);
    Console.WriteLine(report.ToJsonString());
    File.WriteAllText(Path.Combine(directory, "report.json"), report.ToJsonString());
    return report["cases"]!.AsArray().Where(c => c!["memoryEnabled"]!.GetValue<bool>()).All(c => c!["success"]!.GetValue<bool>()) ? 0 : 1;
}

if (FirstCommand(args) == "run")
{
    var suitePath = FirstPositional(args, "run", "suite.json");
    var suite = EvalSuite.Load(suitePath);
    var judge = new Judge();
    RunReport report;
    if (mode == "live")
    {
        var profileName = ParseFlag(args, "--profile", suite.JudgeProfile)
            ?? throw new UsageException("judge profile name is empty; pass --profile <name>");
        var profile = JudgeProfile.LoadBundled(profileName);
        report = await judge.RunLive(suite, profile);
    }
    else
    {
        report = judge.RunRecorded(suite);
    }

    Console.WriteLine(report.ToJson());
    Console.Error.WriteLine(
        $"suite={suite.SuiteId}@{suite.SuiteVersion} mode={mode} mean={report.MeanScore:F3} " +
        $"pass={report.Passed(suite.PassThreshold)} failures={report.Cases.Sum(c => c.Failures.Count)}");
    return report.Passed(suite.PassThreshold) ? 0 : 1;
}

if (FirstCommand(args) == "diff")
{
    var baselinePath = ParseFlag(args, "--baseline", null)
        ?? throw new UsageException("--baseline <baseline.json> is required for diff");
    var tolerance = double.Parse(ParseFlag(args, "--tolerance", "0.1") ?? "0.1");
    var baseline = Baseline.FromJson(File.ReadAllText(baselinePath));
    var reportPath = FirstPositionalAfterFlag(args, "--from")
        ?? throw new UsageException("--from <report.json> is required for diff");
    var current = RunReport.FromJson(File.ReadAllText(reportPath));
    var diff = BaselineDiff.Compute(baseline, current);
    Console.WriteLine(diff.ToJson());
    var passes = diff.GatePasses(current, tolerance);
    Console.Error.WriteLine($"gate={(passes ? "PASS" : "FAIL")} versionMatch={diff.VersionMatch} minDelta={diff.MinDelta:F3}");
    return passes ? 0 : 1;
}

if (FirstCommand(args) == "update-baseline")
{
    var baselinePath = ParseFlag(args, "--baseline", null)
        ?? throw new UsageException("--baseline <baseline.json> is required for update-baseline");
    var reportPath = FirstPositionalAfterFlag(args, "--from")
        ?? throw new UsageException("--from <report.json> is required for update-baseline");
    var report = RunReport.FromJson(File.ReadAllText(reportPath));
    if (report.AnyFailure)
        throw new UsageException("refusing to bless a failing run as the baseline");
    var baseline = Baseline.FromRun(report);
    File.WriteAllText(baselinePath, baseline.ToJson());
    Console.Error.WriteLine($"baseline updated: {baseline.SuiteId}@{baseline.SuiteVersion} cases={baseline.CaseScores.Count}");
    return 0;
}

throw new UsageException("usage: run <suite> [--mode recorded|live] [--profile name] | diff --baseline b --from r [--tolerance 0.1] | update-baseline --baseline b --from r");

static string FirstCommand(string[] args) => args.Length > 0 ? args[0] : "";
static string FirstPositional(string[] args, string command, string fallback)
{
    for (var i = 1; i < args.Length; i++)
    {
        if (args[i].StartsWith("--")) continue;
        return args[i];
    }
    return fallback;
}
static string? FirstPositionalAfterFlag(string[] args, string flag)
{
    for (var i = 0; i < args.Length - 1; i++)
        if (args[i] == flag) return args[i + 1];
    return null;
}
static string? ParseFlag(string[] args, string flag, string? fallback)
{
    for (var i = 0; i < args.Length - 1; i++)
        if (args[i] == flag) return args[i + 1];
    return fallback;
}

sealed class UsageException(string message) : Exception(message);
