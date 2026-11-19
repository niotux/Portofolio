using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Guard;
using PiSharp.Core.Messages;
using PiSharp.Core.Models;

namespace PiSharp.Core.Verification;

/// <summary>
/// Rubric-based review sub-run (pi-sharp-hh-8): a bounded agent loop with its own context that reviews the
/// task's diff against the adapted Codex rubric. It reads the working tree with read-only tools (bash is
/// guarded to refuse mutating commands), then answers with a strict JSON verdict. One blocking budget: a
/// P0/P1 finding with confidence &gt;= <see cref="BlockingConfidence"/> keeps the turn alive once as verifier
/// feedback; anything else is advisory and lands only in the verify- record. A JSON parse failure degrades to
/// an advisory result (FellBack) rather than gating.
/// </summary>
public sealed class RubricReviewer(string cwd, ModelConfig model, ModelConfig? reviewModel, StreamFunction stream,
    IReadOnlyList<IAgentTool>? tools = null)
{
    /// <summary>Maximum assistant turns the sub-run gets (item scope: 8).</summary>
    public const int MaxTurns = 8;

    /// <summary>Output-token cap per model request of the sub-run (item scope: 20k, charged to the task via
    /// the shared seat stream).</summary>
    public const int OutputTokenCap = 20_000;

    /// <summary>Findings at or above this confidence can gate the turn alive once.</summary>
    public const double BlockingConfidence = 0.6;

    public const int DiffCharacterLimit = 40_000;

    public async Task<RubricReviewResult> ReviewAsync(string startSha, string taskSpec, string taskKey, CancellationToken ct = default)
    {
        var active = reviewModel ?? model;
        if (string.IsNullOrWhiteSpace(active.Connection))
            return RubricReviewResult.NotRun("rubric reviewer requires a subscription connection");
        if (!IsSha(startSha)) return RubricReviewResult.NotRun("invalid review start SHA");

        string diff;
        try { diff = await ReadDiffAsync(startSha, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is IOException or InvalidOperationException or OperationCanceledException or TimeoutException)
        {
            return RubricReviewResult.NotRun("could not read patch diff: " + e.Message);
        }
        if (diff.Length == 0) return RubricReviewResult.NotRun("patch diff is empty");

        var usage = Usage.Zero;
        var ranTurns = 0;
        try
        {
            var subTools = tools ?? []; // caller (CliHost) passes read/grep/bash; null = diff-only review
            var guard = new ToolGuard(ReadOnlyRules(), GuardMode.Enforce);
            var context = new AgentContext
            {
                SystemPrompt = SystemPromptText,
                Messages = [new UserMessage(BuildUserMessage(diff, taskSpec))],
                Tools = [.. subTools],
            };
            var config = new AgentLoopConfig
            {
                Model = active,
                MaxTokens = OutputTokenCap,
                Reasoning = ThinkingLevel.Off,
                Temperature = 0,
                ToolGuard = guard,
                ToolCallTimeout = TimeSpan.FromMinutes(2),
                ToolResultTokenBudget = 10_000,
                RecordTurnUsage = (reported, _, _) =>
                {
                    usage = usage.Add(reported);
                    ranTurns++;
                },
                ShouldStopAfterTurn = _ => new ValueTask<bool>(ranTurns >= MaxTurns),
            };

            string? finalText = null;
            await foreach (var ev in AgentLoop.Continue(context, config, stream, ct).ConfigureAwait(false))
            {
                if (ct.IsCancellationRequested) break;
                if (ev is TurnEnd end && end.Message is { } assistant
                    && assistant.StopReason != StopReason.ToolUse)
                {
                    if (assistant.Text.Length > 0) finalText = assistant.Text;
                    if (assistant.StopReason == StopReason.Stop) break;
                }
            }

            if (string.IsNullOrWhiteSpace(finalText))
                return RubricReviewResult.NotRun("rubric review sub-run produced no final answer", TokenCount(usage), diff.Length, ranTurns, null);

            var verdict = ParseVerdict(finalText);
            if (verdict is null)
            {
                // Advisory fallback: the sub-run ran, its text is kept, but nothing gates on it.
                return new RubricReviewResult(true, null, "incorrect", TokenCount(usage), diff.Length, ranTurns,
                    "advisory: verdict JSON could not be parsed; raw answer " + Tail(finalText));
            }
            return new RubricReviewResult(true, verdict, verdict.OverallCorrectness, TokenCount(usage), diff.Length,
                ranTurns, verdict.Reason);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException)
        {
            return RubricReviewResult.NotRun("rubric reviewer unavailable: " + e.Message, TokenCount(usage), diff.Length,
                ranTurns, null);
        }
    }

    /// <summary>FindStartShaAsync lives on BackwardReviewer; the stop gate passes its result through. Kept
    /// here only so the rubric path can be exercised with the same merge-base rule without a second git call.</summary>
    internal static bool IsSha(string value) => value.Length == 40 && value.All(Uri.IsHexDigit);


    /// <summary>Read-only by guard: mutating shell commands are blocked; the reviewer's own tools
    /// (read/grep) are inherently non-mutating.</summary>
    public static IReadOnlyList<GuardRule> ReadOnlyRules()
    {
        // Patterns are evaluated against the serialized tool call (name + JSON args), so they cover the
        // command string of the bash tool regardless of quoting.
        return
        [
            new GuardRule("rubric-review-no-write", "rubric review is read-only: write tools are not available",
                Pattern: "\"(write|edit|apply_patch|patch|notebook)\""),
            new GuardRule("rubric-review-no-mutate", "rubric review is read-only: mutating commands are not allowed",
                Pattern: "\"command\"\\s*:\\s*\"[^\"\\\\]*(rm\\s|git\\s+(commit|push|merge|rebase|reset|checkout\\s+-b)|dd\\s|mkd|mv\\s|cp\\s|tee\\s|truncate|chmod|chown|touch\\s|\\bkill\\b|shutdown|reboot|>\\s*[^\\s&;|<])"),
        ];
    }

    private static string BuildUserMessage(string diff, string taskSpec) =>
        $"Task specification:\n{taskSpec}\n\nDiff to review (merge base with origin/main .. HEAD):\n{diff}\n\n" +
        "Read the surrounding code with the read/grep tools where the diff alone is ambiguous. When done, reply " +
        "with ONLY the verdict JSON described in the rubric — no prose outside the JSON.";

    internal static string SystemPromptText =>
        """
        You are a code reviewer running a bounded rubric review of one code patch. You have read-only tools
        (read, grep, and a bash that refuses mutating commands). Do not modify anything.

        Rubric (adapted from the Codex review rubric):
        - P0: crash, data corruption, or security hole in the changed code (e.g. null dereference on a live path, injection, lost data).
        - P1: the patch breaks or fails the task it was asked to do (wrong behaviour for the specified case, missing the specified fix).
        - P2: real quality risk (resource leak, unhandled error swallowed silently, race condition, missing tests for a new branch).
        - P3: nit or style; report only when clearly wrong.

        Rules:
        - Review only the changed code and its immediate context; do not rewrite the diff's intent.
        - Every finding must cite the file and line range from the diff or the files you read.
        - confidence is your subjective probability the finding is real (0..1). Report a P0/P1 only when confidence >= 0.6.
        - overall_correctness: "correct" (no P0/P1), "partially_correct" (a P0/P1 exists), or "incorrect" (the patch does the wrong thing).
        - No findings: respond with {"overall_correctness":"correct","findings":[],"reason":"..."} (reason one line).
        - Respond with ONLY one JSON object, no markdown fences:
        {"overall_correctness":"correct|partially_correct|incorrect","findings":[{"priority":0,"confidence":0.9,"file":"path","start_line":12,"end_line":14,"summary":"...","evidence":"..."}],"reason":"..."}
        """;

    internal static RubricVerdict? ParseVerdict(string text)
    {
        var start = text.IndexOf('{');
        if (start < 0) return null;
        try
        {
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(text[start..]));
            using var document = JsonDocument.ParseValue(ref reader);
            var node = JsonNode.Parse(document.RootElement.GetRawText()) as JsonObject;
            if (node is null) return null;
            var overall = node["overall_correctness"]?.GetValue<string>();
            var reason = node["reason"]?.GetValue<string>();
            var findings = (node["findings"] as JsonArray)?.Select(f =>
            {
                var obj = f as JsonObject;
                var file = obj?["file"]?.GetValue<string>();
                return new RubricFinding(
                    Priority: obj?["priority"]?.GetValue<int>() ?? -1,
                    Confidence: obj?["confidence"]?.GetValue<double>() ?? 0,
                    File: file ?? "",
                    StartLine: obj?["start_line"]?.GetValue<int>() ?? 0,
                    EndLine: obj?["end_line"]?.GetValue<int>() ?? 0,
                    Summary: obj?["summary"]?.GetValue<string>() ?? "",
                    Evidence: obj?["evidence"]?.GetValue<string>() ?? null);
            })
                .Where(f => f.Priority is >= 0 and <= 3 && f.File.Length > 0)
                .ToList() ?? [];
            if (overall is not ("correct" or "partially_correct" or "incorrect")) return null;
            return new RubricVerdict(overall, findings, reason);
        }
        catch (JsonException) { return null; }
    }

    private async Task<string> ReadDiffAsync(string startSha, CancellationToken ct)
    {
        var (code, output) = await GitAsync(["diff", "--no-ext-diff", "--unified=2", startSha + "..HEAD"], ct).ConfigureAwait(false);
        if (code != 0) throw new InvalidOperationException("git diff exited " + code);
        return output.Length <= DiffCharacterLimit ? output : output[..DiffCharacterLimit] + "\n[diff truncated]";
    }

    private async Task<(int ExitCode, string Output)> GitAsync(IReadOnlyList<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = cwd, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        var result = await PiSharp.Core.Processes.ProcessRunner.RunAsync(psi, TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
        return (result.ExitCode, result.ExitCode == 0 ? result.Output : result.Error);
    }

    private static int TokenCount(Usage usage) => usage.TotalTokens > 0
        ? usage.TotalTokens
        : usage.Input + usage.Output + usage.CacheRead + usage.CacheWrite;

    private static string Tail(string text)
    {
        text = text.Trim();
        return text.Length <= 500 ? text : text[^500..];
    }
}

/// <summary>The parsed verdict of one rubric review.</summary>
public sealed record RubricVerdict(string OverallCorrectness, IReadOnlyList<RubricFinding> Findings, string? Reason)
{
    /// <summary>The findings that gate: P0/P1 with confidence at or above <see cref="RubricReviewer.BlockingConfidence"/>.</summary>
    public IReadOnlyList<RubricFinding> Blocking =>
        Findings.Where(f => f.Priority is 0 or 1 && f.Confidence >= RubricReviewer.BlockingConfidence).ToList();
}

public sealed record RubricFinding(int Priority, double Confidence, string File, int StartLine, int EndLine,
    string Summary, string? Evidence);

/// <summary>Recorded in the verify- record even when the sub-run could not run; only <see cref="Blocking"/>
/// findings gate the turn, once.</summary>
public sealed record RubricReviewResult(bool Ran, RubricVerdict? Verdict, string? OverallCorrectness,
    int Tokens, int DiffCharacters, int Turns, string? Reason)
{
    public IReadOnlyList<RubricFinding> Findings => Verdict?.Findings ?? [];

    public static RubricReviewResult NotRun(string reason, int tokens = 0, int diffCharacters = 0, int turns = 0,
        RubricVerdict? verdict = null) =>
        new(false, verdict, verdict?.OverallCorrectness, tokens, diffCharacters, turns, reason);
}
