using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Messages;
using PiSharp.Core.Models;

namespace PiSharp.Core.Guard;

/// <summary>What the guard decided for one call: the strictest matching rule's decision and the text the model is told.</summary>
public sealed record GuardVerdict(GuardAction Action, GuardRule Rule, string Message);

/// <summary>A reviewer's answer to an Ask: allowed or not, why, and whether the turn must stop (the circuit breaker).</summary>
public sealed record ActionReviewOutcome(bool Allowed, string Rationale, bool StopTurn = false, bool TimedOut = false);

/// <summary>
/// Decides the calls a rule marks Ask (Codex's <c>prompt</c>). A seat runs unattended, so where Codex asks the person,
/// a reviewer decides (Codex auto-review, 2026-10-25, plan-pi-sharp-codex item 2). It is a reviewer swap, never a grant:
/// a Block still blocks.
/// </summary>
public interface IActionReviewer
{
    ValueTask<ActionReviewOutcome> ReviewAsync(ToolCallContent call, GuardVerdict verdict, AgentContext context, CancellationToken ct);
}

/// <summary>
/// The model reviewer: one request on the seat's own model, with no tools, a compact view of the work (the latest user and
/// inbox messages, the seat's latest words, its last tool calls) and the proposed call with the rule that asked. It answers
/// JSON <c>{"decision":"allow"|"deny","rationale":"..."}</c>. A denial goes back to the model with its rationale and the
/// rule "do not reach the same result another way". Three denials in a row, or ten of the last fifty reviews, stop the
/// turn (Codex's circuit breaker); a review that times out or does not answer is a denial that does not count toward it.
/// </summary>
public sealed class ModelActionReviewer(StreamFunction stream, Func<ModelConfig> model) : IActionReviewer
{
    public const int ConsecutiveDenials = 3;
    public const int WindowDenials = 10;
    public const int Window = 50;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(90);
    public int TranscriptCharacters { get; init; } = 12_000;
    public Action<string>? Log { get; init; }

    private readonly object _gate = new();
    private readonly Queue<bool> _recent = new();
    private int _consecutive;

    public async ValueTask<ActionReviewOutcome> ReviewAsync(ToolCallContent call, GuardVerdict verdict, AgentContext context, CancellationToken ct)
    {
        var request = new LlmContext(SystemText, [new UserMessage(Prompt(call, verdict, context))], []);
        string text;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(Timeout);
            var answer = new StringBuilder();
            await foreach (var ev in stream(model(), request, new StreamOptions { MaxTokens = 2_000, Reasoning = ThinkingLevel.Low }, deadline.Token).ConfigureAwait(false))
                if (ev is AssistantDone done) answer.Append(done.Message.Text);
                else if (ev is AssistantError failed) throw new InvalidOperationException(failed.Error.ErrorMessage ?? "review request failed");
            text = answer.ToString();
        }
        catch (Exception ex) when (ex is OperationCanceledException && !ct.IsCancellationRequested || ex is InvalidOperationException or HttpRequestException)
        {
            Log?.Invoke($"[guard] review of {verdict.Rule.Id} did not answer ({ex.Message}); denied, not counted");
            return new ActionReviewOutcome(false, "The reviewer did not answer, so the action was not run; try again later or take it to the lane that decides.", TimedOut: true);
        }
        var (allowed, rationale) = Parse(text);
        bool stop;
        lock (_gate)
        {
            _recent.Enqueue(!allowed);
            while (_recent.Count > Window) _recent.Dequeue();
            _consecutive = allowed ? 0 : _consecutive + 1;
            stop = !allowed && (_consecutive >= ConsecutiveDenials || _recent.Count(d => d) >= WindowDenials);
            if (stop) _consecutive = 0;
        }
        Log?.Invoke($"[guard] review of {verdict.Rule.Id}: {(allowed ? "allowed" : "denied")}{(stop ? " (circuit breaker: turn stopped)" : "")}: {rationale}");
        return new ActionReviewOutcome(allowed, rationale, stop);
    }

    internal static (bool Allowed, string Rationale) Parse(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            try
            {
                if (JsonNode.Parse(text[start..(end + 1)]) is JsonObject json)
                {
                    var decision = json["decision"]?.GetValue<string>()?.Trim().ToLowerInvariant();
                    var rationale = json["rationale"]?.GetValue<string>()?.Trim();
                    if (decision is "allow" or "deny")
                        return (decision == "allow", string.IsNullOrWhiteSpace(rationale) ? "(no rationale given)" : rationale);
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException) { }
        }
        return (false, "The reviewer's answer could not be read, so the action was not run.");
    }

    /// <summary>The proposed call, the rule that asked, and a compact view of the work: user and inbox messages, the
    /// seat's own words and its last tool calls, newest last, cut to <see cref="TranscriptCharacters"/>. No hidden
    /// reasoning, no tool output beyond a line.</summary>
    internal string Prompt(ToolCallContent call, GuardVerdict verdict, AgentContext context)
    {
        var lines = new List<string>();
        foreach (var message in context.Messages.TakeLast(40))
        {
            switch (message)
            {
                case UserMessage user:
                    lines.Add("[user/inbox] " + Cut(ContentText.Of(user.Content), 1_500));
                    break;
                case AssistantMessage assistant:
                    if (assistant.Text.Length > 0) lines.Add("[seat] " + Cut(assistant.Text, 1_000));
                    foreach (var tool in assistant.ToolCalls) lines.Add($"[seat called] {tool.Name} {Cut(tool.Arguments.ToJsonString(), 400)}");
                    break;
                case ToolResultMessage result:
                    lines.Add($"[result{(result.IsError ? ", error" : "")}] " + Cut(ContentText.Of(result.Content), 200));
                    break;
            }
        }
        var transcript = string.Join("\n", lines);
        if (transcript.Length > TranscriptCharacters) transcript = "..." + transcript[^TranscriptCharacters..];
        return $"Work so far (newest last):\n{transcript}\n\nThe seat now wants to run:\n{call.Name} {Cut(call.Arguments.ToJsonString(), 4_000)}\n\n"
            + $"Rule that asks for review: [{verdict.Rule.Id}] {verdict.Rule.Message}\n\n"
            + "Answer with only the JSON {\"decision\":\"allow\"|\"deny\",\"rationale\":\"one or two sentences\"}.";
    }

    private static string Cut(string text, int max) =>
        text.Length <= max ? text.ReplaceLineEndings(" ") : text[..max].ReplaceLineEndings(" ") + "...";

    public const string SystemText =
        "You review one action an unattended coding agent wants to take, because a rule of the factory marks it for review. "
        + "You decide only whether this one action should run now. You do not do the work and you do not suggest other work.\n"
        + "Allow it when it serves the task the agent was given, stays inside its own work (its checkout, its branch, its "
        + "work item), and can be undone or is plainly intended by its instructions.\n"
        + "Deny it when it would: send secrets or credentials anywhere, or probe for them; weaken security broadly or "
        + "persistently; destroy work or data that cannot be rebuilt (other people's branches, evidence, databases, history); "
        + "act on shared infrastructure the agent does not own; or reach a result its rules forbid by another route.\n"
        + "When in doubt about something irreversible, deny and say what would make it safe. Keep the rationale short and "
        + "concrete: it is read by the agent.";
}
