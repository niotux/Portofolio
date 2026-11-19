using System.Collections.Concurrent;
using System.Text;
using PiSharp.Core.Agent;
using PiSharp.Core.Messages;
using PiSharp.Core.Models;
using PiSharp.Llm.OpenAi;

namespace PiSharp.MemoryEval;

/// <summary>
/// The judge. Two modes, as in ai-blue's two-layer eval:
///
///   recorded : no model, no network. The case's committed <c>recordedResponse</c> is judged -
///              deterministic checks plus the concept-coverage heuristic. This is how the suite
///              runs in CI (the deterministic gate layer) with zero live calls.
///
///   live     : the case's prompt is sent to the judge profile's model (the real seat memory
///              context would be inlined into the prompt by the pipeline; the scaffold sends the
///              case prompt as-is) and the response is judged the same way. Opt-in.
///
/// The judge score in [0,1] is a 0-2 per-dimension rubric (ai-blue's scoring) normalized to
/// [0,1] and averaged across the case's dimension. The deterministic checks are the hard gate;
/// the score is the quality signal the baseline tracks.
/// </summary>
public sealed class Judge
{
    /// <summary>Concept-coverage rubric: a case's <c>requiredConcepts</c> each count one point.</summary>
    public static EvalCaseResult JudgeCase(EvalCase c, string response)
    {
        var failures = DeterministicChecks.Check(c, response);
        var covered = new List<string>();
        var missing = new List<string>();
        foreach (var concept in c.RequiredConcepts ?? [])
        {
            if (ContainsConcept(response, concept)) covered.Add(concept); else missing.Add(concept);
        }

        double? score = null;
        if ((c.RequiredConcepts ?? []).Count > 0)
            score = (double)covered.Count / (c.RequiredConcepts ?? []).Count;

        var status = failures.Count > 0
            ? EvalStatus.Fail
            : (score is { } s && s >= 0.999
                ? EvalStatus.Pass
                : EvalStatus.Scored);

        return new EvalCaseResult
        {
            CaseId = c.Id,
            Status = status,
            Score = score,
            Failures = failures,
            CoveredConcepts = covered,
            MissingConcepts = missing,
        };
    }

    /// <summary>Case-insensitive concept match on the response body.</summary>
    private static bool ContainsConcept(string response, string concept) =>
        response.ToLowerInvariant().Contains(concept.ToLowerInvariant());

    /// <summary>
    /// Runs one suite in recorded mode: every case must carry a <c>recordedResponse</c>, none is
    /// sent to a model. This is the CI path and the default; it never touches the network.
    /// </summary>
    public RunReport RunRecorded(EvalSuite suite)
    {
        var results = new List<EvalCaseResult>();
        foreach (var c in suite.Cases)
        {
            if (c.RecordedResponse is null)
                throw new InvalidOperationException(
                    $"Case {c.Id} has no recordedResponse; recorded mode needs one (CI gate).");
            results.Add(JudgeCase(c, c.RecordedResponse));
        }
        return MakeReport(suite, results, "recorded");
    }

    /// <summary>
    /// Runs one suite in live mode: each case's prompt is sent to the judge profile's model and
    /// the response is judged. Opt-in; needs a reachable model. Failures on the model are
    /// recorded as deterministic failures so the run still produces a report (and a red gate).
    /// </summary>
    public async Task<RunReport> RunLive(EvalSuite suite, JudgeProfile profile, CancellationToken ct = default)
    {
        var client = new OpenAiCompletionsClient();
        var model = profile.ToModelConfig();
        var options = profile.ToStreamOptions();
        var results = new List<EvalCaseResult>();

        foreach (var c in suite.Cases)
        {
            var ctx = new LlmContext(
                "You are a memory-accurate coding assistant. Answer only from the context given. " +
                "If the context lacks a fact, say it is not in context - do not invent one.",
                [new UserMessage(c.Prompt)],
                []);

            string? response = null;
            var failures = new List<string>();
            try
            {
                response = await CollectText(client, model, ctx, options, ct);
            }
            catch (Exception e)
            {
                failures.Add($"judge call failed: {e.Message}");
            }

            if (response is null)
            {
                results.Add(new EvalCaseResult
                {
                    CaseId = c.Id,
                    Status = EvalStatus.Fail,
                    Failures = failures,
                });
                continue;
            }

            var judged = JudgeCase(c, response);
            judged.Failures = failures.Concat(judged.Failures).ToList();
            if (judged.Failures.Count > 0 && judged.Status != EvalStatus.Fail)
                judged.Status = EvalStatus.Fail;
            results.Add(judged);
        }

        return MakeReport(suite, results, "live");
    }

    private static RunReport MakeReport(EvalSuite suite, List<EvalCaseResult> results, string mode) => new()
    {
        SuiteId = suite.SuiteId,
        SuiteVersion = suite.SuiteVersion,
        JudgeProfile = suite.JudgeProfile,
        Mode = mode,
        RunAt = DateTimeOffset.UtcNow.ToString("o"),
        Cases = results,
    };

    /// <summary>Collects the assistant's text from a stream (the same event contract pi-sharp uses).</summary>
    private static async Task<string> CollectText(
        OpenAiCompletionsClient client, ModelConfig model, LlmContext ctx, StreamOptions options, CancellationToken ct)
    {
        var sb = new StringBuilder();
        await foreach (var ev in client.Stream(model, ctx, options, ct))
        {
            if (ev is TextDelta d) sb.Append(d.Delta);
            else if (ev is AssistantDone done) break;
        }
        return sb.ToString();
    }
}
