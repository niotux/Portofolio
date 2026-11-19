using System.Text;
using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Mcp;

namespace PiSharp.Core.Factory;

/// <summary>
/// The goal audits on kb_transition, enforced (plan-pi-sharp-codex items 14-15; Codex's goal completion and blocked audits).
/// The completion audit: a seat hands an epic in to review
/// only with each acceptance criterion and the evidence that proves it, as kb_transition's <c>criteria</c>. A hand-in without
/// them, or with a criterion that failed or has no evidence, is refused here with the epic's own text, before the KB sees it.
/// It applies only where the KB's kb_transition takes criteria (hub 1.0.286 on), so an older hub never strands a seat.
/// </summary>
public sealed class HandInGate(IAgentTool inner, Func<string, CancellationToken, Task<FactoryHandIn?>> facts, TimeProvider? clock = null,
    Func<string, CancellationToken, Task<bool>>? enabled = null) : IAgentTool
{
    /// <summary>The factory switches that turn each audit on (the Engine Room page's seat behaviours).</summary>
    public const string HandInSwitch = "handin-criteria";
    public const string BlockedSwitch = "blocked-audit";

    private Task<bool> OnAsync(string name, CancellationToken ct) => enabled is null ? Task.FromResult(true) : enabled(name, ct);

    /// <summary>Moves to blocked on a person or a thing refused before one passes (Codex's blocked audit: blocked only after the
    /// same blocker three times).</summary>
    public const int BlockedAuditRefusals = 2;
    /// <summary>How long an earlier blocked attempt on the same item still counts.</summary>
    public static readonly TimeSpan BlockedAuditWindow = TimeSpan.FromHours(6);

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Dictionary<string, List<DateTimeOffset>> _blockedAttempts = new(StringComparer.Ordinal);

    public string Name => inner.Name;
    public string Label => inner.Label;
    public string Description => inner.Description;
    public JsonObject Parameters
    {
        get
        {
            var schema = (JsonObject)inner.Parameters.DeepClone();
            if (schema["properties"] is JsonObject props && props.ContainsKey("criteria"))
                props["criteria_revision"] = new JsonObject { ["type"] = "string", ["description"] = "The acceptance manifest revision returned by a refused hand-in; required with canonical criterion IDs." };
            return schema;
        }
    }
    public ToolExecutionMode? ExecutionMode => inner.ExecutionMode;
    public TimeSpan? MaxDuration => inner.MaxDuration;
    public JsonNode? PrepareArguments(JsonNode? arguments) => inner.PrepareArguments(arguments);

    /// <summary>The KB's kb_transition takes the criteria list.</summary>
    public bool KbTakesCriteria => inner.Parameters["properties"] is JsonObject props && props.ContainsKey("criteria");

    public async Task<AgentToolResult> ExecuteAsync(string toolCallId, JsonObject arguments, Action<AgentToolResult>? onUpdate,
        CancellationToken cancellationToken)
    {
        var status = (arguments["to_status"] ?? arguments["status"])?.GetValue<string>();
        var slug = (arguments["work_item"] ?? arguments["slug"])?.GetValue<string>();
        if (status == "blocked" && slug is { Length: > 0 } && await OnAsync(BlockedSwitch, cancellationToken).ConfigureAwait(false)
            && BlockedAudit(slug, arguments) is { } audit)
            return AgentToolResult.Error(audit);
        if (status == "review" && slug is { Length: > 0 } && KbTakesCriteria && await OnAsync(HandInSwitch, cancellationToken).ConfigureAwait(false))
        {
            var epic = await facts(slug, cancellationToken).ConfigureAwait(false);
            if (epic is null) return AgentToolResult.Error("Acceptance manifest unavailable; retry the hand-in after the epic can be read.");
            if (epic.IsEpic && Refusal(slug, epic.Objective, arguments["criteria"], epic.Criteria, arguments["criteria_revision"]?.GetValue<string>()) is { } refusal)
                return AgentToolResult.Error(refusal);
        }
        var forwarded = (JsonObject)arguments.DeepClone();
        if (inner.Parameters["properties"]?["criteria_revision"] is null) forwarded.Remove("criteria_revision");
        return await inner.ExecuteAsync(toolCallId, forwarded, onUpdate, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The blocked audit: a wait on another work item (blocked_by) is a dependency and passes; a wait on a person or a
    /// thing passes only on the third attempt within <see cref="BlockedAuditWindow"/>, after two refusals that ask the questions
    /// rule work-item-assignment asks first (a person is the last resort, never the default gate).</summary>
    private string? BlockedAudit(string slug, JsonObject arguments)
    {
        if (arguments["blocked_by"]?.GetValue<string>() is { Length: > 0 }) return null;
        var now = _clock.GetUtcNow();
        int count;
        lock (_blockedAttempts)
        {
            if (!_blockedAttempts.TryGetValue(slug, out var attempts)) _blockedAttempts[slug] = attempts = [];
            attempts.RemoveAll(at => now - at > BlockedAuditWindow);
            attempts.Add(now);
            count = attempts.Count;
            if (count > BlockedAuditRefusals) attempts.Clear();
        }
        if (count > BlockedAuditRefusals) return null;
        return $"Not blocked yet ({count} of {BlockedAuditRefusals + 1}): before {slug} waits on a person, audit the blocker. " +
            "1. Can a lane decide it? Product questions are the product owner's, design the architects', placement the team lead's: ask them in their lane and keep working. " +
            "2. Can evidence the team holds or can produce stand in for the check you are waiting for (a test, a demo seat's capture, a reference in the repository)? " +
            "3. Can you move on with the parts that do not need it, and come back to it? " +
            "If the epic waits on another work item, block it with blocked_by=<that item> (it passes at once). " +
            $"If after this it is still blocked on the same thing, make the same move again: the {BlockedAuditRefusals + 1}th time it is recorded.";
    }

    /// <summary>Why the hand-in is not one yet; null when every criterion passed with its evidence.</summary>
    public static string? Refusal(string slug, string objective, JsonNode? criteria,
        IReadOnlyList<AcceptanceCriterion>? canonical = null, string? revision = null)
    {
        var problems = new List<string>();
        canonical ??= AcceptanceCriteria.Read(null, objective);
        var manifestRevision = AcceptanceCriteria.Revision(canonical);
        if (canonical.Count == 0) problems.Add("no canonical acceptance criteria are available; define acceptance_criteria on the epic");
        if (canonical.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != canonical.Count || canonical.Any(c => string.IsNullOrWhiteSpace(c.Id)))
            problems.Add("canonical acceptance criterion IDs must be unique and nonempty");
        if (revision is not null && revision != manifestRevision) problems.Add("acceptance criteria revision changed; use the current manifest below");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (criteria is not JsonArray list || list.Count == 0) problems.Add("no criteria were given");
        else
            foreach (var (c, i) in list.Select((c, i) => (c as JsonObject, i + 1)))
            {
                var name = c?["criterion"]?.GetValue<string>();
                var result = c?["result"]?.GetValue<string>()?.Trim().ToLowerInvariant();
                var evidence = c?["evidence"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    var matches = canonical.Where(k => k.Id == name || k.Text == AcceptanceCriteria.Normalize(name)).ToArray();
                    if (matches.Length != 1) problems.Add($"unknown or ambiguous criterion '{name}'");
                    else
                    {
                        if (!seen.Add(matches[0].Id)) problems.Add($"duplicate criterion '{name}'");
                        if (name == matches[0].Id && revision is null) problems.Add("canonical IDs require criteria_revision");
                    }
                }
                if (string.IsNullOrWhiteSpace(name)) problems.Add($"criterion {i} has no text");
                else if (string.IsNullOrWhiteSpace(evidence)) problems.Add($"'{name}' has no evidence");
                else if (result == "fail") problems.Add($"'{name}' failed: keep the epic in progress until it passes");
                else if (result != "pass") problems.Add($"'{name}' has no result (pass or fail)");
            }
        foreach (var missing in canonical.Where(c => !seen.Contains(c.Id))) problems.Add($"missing criterion '{missing.Text}'");
        if (problems.Count == 0) return null;
        var sb = new StringBuilder($"Not handed in: {string.Join("; ", problems)}. ");
        sb.Append($"Hand {slug} in criterion by criterion: kb_transition work_item={slug} to_status=review criteria=[{{criterion, result: \"pass\", evidence}}], ");
        sb.Append("one for each acceptance criterion below, each with what proves it now: a kb_suite run id and the tests, a demo, a capture. ");
        sb.Append("A criterion you cannot prove yet is work still to do, or a question for the product owner.\n\nThe epic:\n").Append(objective);
        sb.Append("\n\nAcceptance manifest revision: ").Append(manifestRevision);
        foreach (var criterion in canonical) sb.Append('\n').Append(criterion.Id).Append(": ").Append(criterion.Text);
        return sb.ToString();
    }
}
