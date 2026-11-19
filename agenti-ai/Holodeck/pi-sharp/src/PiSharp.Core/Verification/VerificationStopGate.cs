using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Messages;
using PiSharp.Core.Notebook;

namespace PiSharp.Core.Verification;

/// <summary>Runs task checks when an attached agent would otherwise stop. Failed checks keep the turn alive
/// and provide a single replaceable feedback message for the next turn.</summary>
public sealed class VerificationStopGate(Verifier verifier, NotebookStore notebook, Func<string?> taskKey,
    int maxAttempts, Func<string, string, int, int, CancellationToken, Task>? capCallback = null,
    BackwardReviewer? backwardReviewer = null, RubricReviewer? rubricReviewer = null)
{
    private readonly object _gate = new();
    private string? _activeTask;
    private int _attempts;
    private string? _feedback;
    private string? _deliveredFeedback;
    private bool _feedbackPending;
    private bool _rubricBlockConsumed;
    private string? _rubricFeedback;
    private string? _deliveredRubricFeedback;

    public string? LastFailure { get; private set; }

    /// <summary>Returns true only when the caller may proceed with its normal stop handling. A false result
    /// means a retry turn was scheduled, except when the retry cap was reached (LastFailure is then set).</summary>
    public async ValueTask<bool> ShouldStopAfterTurnAsync(TurnContext turn, CancellationToken cancellationToken = default)
    {
        var task = taskKey();
        if (task is not { Length: > 0 } || turn.Message.ToolCalls.Any() || verifier.ChecksFor(task).Count == 0)
            return true;

        var current = await WorkspaceEvidence.CaptureAsync(verifier.Cwd, cancellationToken).ConfigureAwait(false);
        if (HasPassingRecord(task, current)) return true;

        var report = await verifier.RunAllAsync(task, cancellationToken).ConfigureAwait(false);
        string? startSha = null;
        string taskSpec = string.Join("\n\n", turn.Context.Messages.OfType<UserMessage>()
            .Select(m => m.Text).Where(s => !string.IsNullOrWhiteSpace(s)).Take(3));
        if (report.Passed && backwardReviewer is not null)
        {
            startSha = await backwardReviewer.FindStartShaAsync(cancellationToken).ConfigureAwait(false);
            var review = startSha is null
                ? BackwardReviewResult.NotRun("could not find feature-branch start SHA")
                : await backwardReviewer.ReviewAsync(startSha, taskSpec, cancellationToken).ConfigureAwait(false);
            report = report with { BackwardReview = review };
        }
        if (report.Passed && rubricReviewer is not null && !RubricBlockConsumed(task))
        {
            startSha ??= (backwardReviewer is not null ? startSha
                : await FindStartShaAsync(verifier.Cwd, cancellationToken).ConfigureAwait(false));
            var review = startSha is null
                ? RubricReviewResult.NotRun("could not find feature-branch start SHA")
                : await rubricReviewer.ReviewAsync(startSha, taskSpec, task, cancellationToken).ConfigureAwait(false);
            report = report with { RubricReview = review };
        }
        VerificationRecords.Record(notebook, report);
        if (report.Passed && report.RubricReview is { Ran: true, Verdict: not null } rubric
            && rubric.Verdict.Blocking.Count > 0)
        {
            // One blocking budget: the P0/P1 finding keeps the turn alive exactly once per task, as a
            // replaceable feedback message in the existing next-turn composition.
            lock (_gate)
            {
                if (TaskMatches(task)) _rubricBlockConsumed = true;
                _rubricFeedback = RubricFeedback(task, report, rubric);
                _feedback = _rubricFeedback;
                _feedbackPending = true;
                _attempts++;
            }
            LastFailure = "rubric review blocking finding: " + string.Join("; ",
                rubric.Verdict.Blocking.Select(f => $"P{f.Priority} {f.File}:{f.StartLine} {f.Summary}"));
            return false;
        }
        if (report.Passed)
        {
            LastFailure = null;
            return true;
        }

        int attempts;
        string failure;
        bool capped;
        lock (_gate)
        {
            if (!string.Equals(_activeTask, task, StringComparison.Ordinal))
            {
                _activeTask = task;
                _attempts = 0;
                _feedback = null;
                _deliveredFeedback = null;
                _feedbackPending = false;
                _rubricBlockConsumed = false;
                _rubricFeedback = null;
                _deliveredRubricFeedback = null;
                LastFailure = null;
            }
            attempts = ++_attempts;
            failure = LastFailure = report.Summary();
            capped = attempts >= Math.Max(1, maxAttempts);
            if (!capped)
            {
                _feedback = Feedback(task, report);
                _feedbackPending = true;
            }
        }
        if (!capped) return false;
        if (capCallback is not null)
            await capCallback(task, failure, attempts, Math.Max(1, maxAttempts), cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Composes the loop's existing next-turn hook. Previous verifier feedback is removed from the
    /// conversation before the latest result is attached, preventing an unbounded stack of failures.</summary>
    public Func<TurnContext, ValueTask<TurnUpdate?>> ComposePrepareNextTurn(
        Func<TurnContext, ValueTask<TurnUpdate?>>? prepare)
    {
        return async turn =>
        {
            var update = prepare is null ? null : await prepare(turn).ConfigureAwait(false);
            string? previous;
            string? latest;
            string? rubricPrevious;
            string? rubricLatest;
            lock (_gate)
            {
                previous = _deliveredFeedback;
                latest = _feedbackPending ? _feedback : null;
                if (latest is not null) _deliveredFeedback = latest;
                _feedbackPending = false;
                rubricPrevious = _deliveredRubricFeedback;
                rubricLatest = _feedbackPending ? _rubricFeedback : null;
                if (rubricLatest is not null && rubricLatest != latest) _deliveredRubricFeedback = rubricLatest;
            }
            if (latest is null && rubricLatest is null) return update;
            if (previous is not null)
                turn.Context.Messages.RemoveAll(m => m is UserMessage user && user.Text == previous);
            if (rubricPrevious is not null)
                turn.Context.Messages.RemoveAll(m => m is UserMessage user && user.Text == rubricPrevious);
            var trailing = new List<Message>(update?.Trailing ?? []);
            if (latest is not null) trailing.Add(new UserMessage(latest));
            if (rubricLatest is not null) trailing.Add(new UserMessage(rubricLatest));
            return (update ?? new TurnUpdate()) with { Trailing = trailing };
        };
    }

    private bool HasPassingRecord(string task, JsonObject current)
    {
        if (current["available"]?.GetValue<bool>() != true || current["sha256"]?.GetValue<string>() is not { Length: 64 } currentHash)
            return false;
        var identity = verifier.EvidenceIdentityFor(task);
        var prefix = VerificationRecords.IdPrefix(task);
        foreach (var row in notebook.List().Where(r => r["id"]?.GetValue<string>()?.StartsWith(prefix, StringComparison.Ordinal) == true))
        {
            try
            {
                var text = row["text"]?.GetValue<string>();
                if (text is null) continue;
                var record = JsonNode.Parse(text);
                if (record?["passed"]?.GetValue<bool>() != true) continue;
                if (record["checkIdentity"]?.GetValue<string>() != identity) continue;
                var results = record["results"] as JsonArray;
                var after = results?.LastOrDefault()?["after"] as JsonObject;
                if (after?["available"]?.GetValue<bool>() == true && after["sha256"]?.GetValue<string>() == currentHash) return true;
            }
            catch (System.Text.Json.JsonException) { }
        }
        return false;
    }

    private static string Feedback(string task, VerificationReport report) =>
        $"Harness verification failed for {task}. Fix the reported checks, then stop again to rerun them.\n{report.Summary()}";

    private static string RubricFeedback(string task, VerificationReport report, RubricReviewResult rubric) =>
        $"Rubric review found a blocking finding in the diff for {task} (confidence >= {RubricReviewer.BlockingConfidence}). " +
        "Fix or justify each finding below, then stop again; the rubric review will re-run.\n" +
        string.Join('\n', rubric.Verdict!.Blocking.Select(f =>
            $"P{f.Priority} ({f.Confidence:F2}) {f.File}:{f.StartLine}-{f.EndLine} — {f.Summary}")) +
        "\n" + report.Summary();

    private bool RubricBlockConsumed(string task)
    {
        lock (_gate)
            return string.Equals(_activeTask, task, StringComparison.Ordinal) && _rubricBlockConsumed;
    }

    private bool TaskMatches(string task)
    {
        if (!string.Equals(_activeTask, task, StringComparison.Ordinal))
        {
            _activeTask = task;
            _attempts = 0;
            _feedback = null;
            _deliveredFeedback = null;
            _feedbackPending = false;
            _rubricBlockConsumed = false;
            _rubricFeedback = null;
            _deliveredRubricFeedback = null;
            LastFailure = null;
            return true;
        }
        return true;
    }

    private static async Task<string?> FindStartShaAsync(string cwd, CancellationToken ct)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git")
        {
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("merge-base");
        psi.ArgumentList.Add("refs/remotes/origin/main");
        psi.ArgumentList.Add("HEAD");
        try
        {
            var result = await PiSharp.Core.Processes.ProcessRunner.RunAsync(psi, TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
            var sha = result.Output.Trim();
            return result.ExitCode == 0 && sha.Length == 40 && sha.All(Uri.IsHexDigit) ? sha : null;
        }
        catch (System.OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is System.IO.IOException or System.InvalidOperationException or TimeoutException)
        {
            return null;
        }
    }
}
