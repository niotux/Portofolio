using System.Diagnostics;
using System.Text;
using PiSharp.Core.Agent;
using PiSharp.Core.Messages;
using PiSharp.Core.Models;

namespace PiSharp.Core.Verification;

/// <summary>Optional RETRACE-style advisory: infer the patch's problem without the task, then compare that
/// inference with the task specification in a separate, cheap request. It never decides verification success.</summary>
public sealed class BackwardReviewer(string cwd, ModelConfig model, StreamFunction stream)
{
    public const int DiffCharacterLimit = 16_000;
    public const int ReviewMaxTokens = 192;

    public async Task<BackwardReviewResult> ReviewAsync(string startSha, string taskSpec, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(model.Connection))
            return BackwardReviewResult.NotRun("reviewer requires a subscription connection");
        if (!IsSha(startSha)) return BackwardReviewResult.NotRun("invalid review start SHA");

        string diff;
        try { diff = await ReadDiffAsync(startSha, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is IOException or InvalidOperationException or OperationCanceledException or TimeoutException)
        {
            return BackwardReviewResult.NotRun("could not read patch diff: " + e.Message);
        }
        if (diff.Length == 0) return BackwardReviewResult.NotRun("patch diff is empty");

        var totalUsage = Usage.Zero;
        try
        {
            var inferred = await CompleteAsync(
                "Infer the concrete problem this code patch appears to solve. You have no issue or task description; use only the diff. Reply with one concise sentence.",
                diff, ct).ConfigureAwait(false);
            totalUsage = totalUsage.Add(inferred.Usage);
            if (string.IsNullOrWhiteSpace(inferred.Text))
                return BackwardReviewResult.NotRun("backward inference returned no text", TokenCount(totalUsage), diff.Length);

            var comparison = await CompleteAsync(
                "Compare the inferred problem with the task specification. Decide whether the patch addresses the requested task. Start with exactly MATCH or MISMATCH, then give one concise reason. Be conservative: MISMATCH only when they are materially different.",
                $"Task title/spec:\n{taskSpec}\n\nProblem inferred from patch alone:\n{inferred.Text}", ct).ConfigureAwait(false);
            totalUsage = totalUsage.Add(comparison.Usage);
            var line = comparison.Text.Split('\n', 2)[0].Trim();
            bool? mismatch = line.StartsWith("MISMATCH", StringComparison.OrdinalIgnoreCase) ? true
                : line.StartsWith("MATCH", StringComparison.OrdinalIgnoreCase) ? false : null;
            return new BackwardReviewResult(true, mismatch,
                mismatch == true ? "Backward reviewer mismatch warning: " + comparison.Text.Trim() : null,
                inferred.Text.Trim(), TokenCount(totalUsage), diff.Length, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException)
        {
            return BackwardReviewResult.NotRun("backward reviewer unavailable: " + e.Message, TokenCount(totalUsage), diff.Length);
        }
    }

    /// <summary>Uses the feature branch's merge base with origin/main as the task's diff start. If that ref is
    /// unavailable (e.g. an offline clone), returns null rather than widening the review to unrelated history.</summary>
    public async Task<string?> FindStartShaAsync(CancellationToken ct = default)
    {
        try
        {
            var (code, output) = await GitAsync(["merge-base", "refs/remotes/origin/main", "HEAD"], ct).ConfigureAwait(false);
            var sha = output.Trim();
            return code == 0 && IsSha(sha) ? sha : null;
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or OperationCanceledException or TimeoutException) { return null; }
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

    private async Task<(string Text, Usage Usage)> CompleteAsync(string system, string user, CancellationToken ct)
    {
        var context = new LlmContext(system, [new UserMessage(user)], []);
        var options = new StreamOptions { MaxTokens = ReviewMaxTokens, Reasoning = ThinkingLevel.Off, Temperature = 0, ApiKey = model.ResolveApiKey() };
        var text = new StringBuilder();
        Usage usage = Usage.Zero;
        await foreach (var ev in stream(model, context, options, ct).WithCancellation(ct).ConfigureAwait(false))
        {
            if (ev is TextDelta delta) text.Append(delta.Delta);
            else if (ev is AssistantDone done)
            {
                if (text.Length == 0) text.Append(done.Message.Text);
                usage = done.Message.Usage;
            }
            else if (ev is AssistantError error) throw new InvalidOperationException(error.Error.ErrorMessage ?? "model request failed");
        }
        return (text.ToString(), usage);
    }

    private static bool IsSha(string value) => value.Length == 40 && value.All(Uri.IsHexDigit);

    private static int TokenCount(Usage usage) => usage.TotalTokens > 0
        ? usage.TotalTokens
        : usage.Input + usage.Output + usage.CacheRead + usage.CacheWrite;
}

/// <summary>Recorded even when an optional review could not run; a warning is advisory and must not gate.</summary>
public sealed record BackwardReviewResult(bool Ran, bool? Mismatch, string? Warning, string? InferredProblem,
    int Tokens, int DiffCharacters, string? Reason)
{
    public static BackwardReviewResult NotRun(string reason, int tokens = 0, int diffCharacters = 0) =>
        new(false, null, null, null, tokens, diffCharacters, reason);
}
