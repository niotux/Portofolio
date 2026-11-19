using System.Text.Json;
using System.Text.Json.Nodes;
using PiSharp.Core.Notebook;

namespace PiSharp.Core.Verification;

/// <summary>Writes the verification evidence into the seat's notebook as records named
/// <c>verify-&lt;task&gt;-&lt;n&gt;</c> (kind <c>check</c>). The IDs live in <c>NotebookTool.IsRuntimeEvidence</c>,
/// so the model's own <c>notebook put/write/transition</c> on them is refused: the harness is the only
/// writer of verification evidence.</summary>
public static class VerificationRecords
{
    public const string VerifyPrefix = "verify-";

    public static string IdPrefix(string task) => VerifyPrefix + task + "-";

    /// <summary>Records one run. Existing verify records for the task are left alone (history); a new
    /// verify-&lt;task&gt;-&lt;n&gt; is appended with the next n. Returns the new record's id.</summary>
    public static string Record(NotebookStore store, VerificationReport report)
    {
        var prefix = IdPrefix(report.Task);
        var n = store.List().Where(r => r["id"]!.GetValue<string>().StartsWith(prefix, StringComparison.Ordinal))
            .Select(r => r["id"]!.GetValue<string>()[prefix.Length..])
            .Select(s => int.TryParse(s, out var i) ? i : -1)
            .DefaultIfEmpty(0)
            .Max() + 1;
        var id = prefix + n;
        var json = new JsonObject
        {
            ["task"] = report.Task,
            ["passed"] = report.Passed,
            ["checkIdentity"] = report.CheckIdentity,
            ["reason"] = report.Reason,
            ["head"] = report.Results.FirstOrDefault(r => r.After["head"]?.GetValue<string>() is { Length: > 0 } h)?.After["head"]!.GetValue<string>() ?? "",
            ["results"] = new JsonArray(report.Results.Select(ToJson).ToArray()),
            ["summary"] = ReviewSummary(report),
            ["backwardReview"] = report.BackwardReview is { } review ? new JsonObject
            {
                ["ran"] = review.Ran,
                ["mismatch"] = review.Mismatch,
                ["warning"] = review.Warning,
                ["inferredProblem"] = review.InferredProblem,
                ["tokens"] = review.Tokens,
                ["diffCharacters"] = review.DiffCharacters,
                ["reason"] = review.Reason,
            } : null,
            ["rubricReview"] = report.RubricReview is { } rubric ? new JsonObject
            {
                ["ran"] = rubric.Ran,
                ["overallCorrectness"] = rubric.OverallCorrectness,
                ["tokens"] = rubric.Tokens,
                ["diffCharacters"] = rubric.DiffCharacters,
                ["turns"] = rubric.Turns,
                ["reason"] = rubric.Reason,
                ["findings"] = new JsonArray(rubric.Findings.Select(f => new JsonObject
                {
                    ["priority"] = f.Priority,
                    ["confidence"] = f.Confidence,
                    ["file"] = f.File,
                    ["startLine"] = f.StartLine,
                    ["endLine"] = f.EndLine,
                    ["summary"] = f.Summary,
                    ["evidence"] = f.Evidence,
                    ["blocking"] = f.Priority is 0 or 1 && f.Confidence >= RubricReviewer.BlockingConfidence,
                }).ToArray()),
            } : null,
            ["at"] = DateTimeOffset.UtcNow.ToString("O"),
            ["checkSource"] = report.CheckSource is { } provenance ? new JsonObject
            {
                ["source"] = provenance.Source,
                ["revision"] = provenance.Revision,
                ["sha256"] = provenance.Sha256,
                ["approved"] = provenance.IsValid,
            } : null,
        };
        store.Put(id, "check", json.ToJsonString(), report.Task, "open", 0);
        try { NotebookLessons.FromVerification(store, report, id); }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or JsonException)
        { /* Derived learning must never affect the authoritative verification receipt. */ }
        return id;
    }

    private static JsonObject ToJson(VerificationResult r) => new()
    {
        ["id"] = r.CheckId,
        ["passed"] = r.Passed,
        ["exitCode"] = r.ExitCode,
        ["pinnedUnchanged"] = r.PinnedUnchanged,
        ["outputTail"] = r.OutputTail,
        ["before"] = r.Before.DeepClone(),
        ["after"] = r.After.DeepClone(),
        ["head"] = r.Head,
        ["at"] = r.At.ToString("O"),
    };

    private static string ReviewSummary(VerificationReport report)
    {
        BackwardReviewResult? review = report.BackwardReview;
        RubricReviewResult? rubric = report.RubricReview;
        if (review is null && rubric is null)
            return report.Summary();
        var lines = new List<string> { report.Summary() };
        if (review is not null)
        {
            lines.Add($"backward reviewer tokens {review.Tokens}");
            if (review.Warning is { Length: > 0 } warning) lines.Add(warning);
            else if (review.Reason is { Length: > 0 } reason) lines.Add("backward reviewer unavailable: " + reason);
        }
        if (rubric is not null)
        {
            lines.Add($"rubric review tokens {rubric.Tokens}");
            if (rubric.Ran)
            {
                if (rubric.Verdict is { } verdict && verdict.Reason is { Length: > 0 } verdictReason)
                    lines.Add("rubric review: " + verdictReason);
                else if (rubric.Reason is { Length: > 0 } rubricReason) lines.Add(rubricReason);
            }
            else if (rubric.Reason is { Length: > 0 } unavailable) lines.Add("rubric review unavailable: " + unavailable);
        }
        return string.Join('\n', lines);
    }
}
