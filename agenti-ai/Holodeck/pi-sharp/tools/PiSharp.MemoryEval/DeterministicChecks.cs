namespace PiSharp.MemoryEval;

/// <summary>
/// Deterministic checks on a case's response. These run in BOTH modes (recorded and live) and
/// a single failure fails the case outright - style quality cannot compensate for a leak.
/// The checks are the enforcement point for the ADR's four pipeline properties:
///   non-fabrication  -> forbidden phrases (the model must not invent what memory lacks)
///   privacy          -> forbidden memory ids must not leak into the response
///   persona isolation-> foreign seat/tenant memory ids must not surface
///   retrieval        -> expected memory ids must be present (the recall leg worked)
/// </summary>
public static class DeterministicChecks
{
    /// <summary>
    /// Runs the hard checks for one case. Returns the list of failures (empty = all green).
    /// A case with failures gets <see cref="EvalStatus.Fail"/> regardless of its judge score.
    /// </summary>
    public static List<string> Check(EvalCase c, string response)
    {
        var failures = new List<string>();
        var lower = response.ToLowerInvariant();

        // Privacy / isolation: a forbidden memory id must not appear in the response.
        foreach (var id in c.ForbiddenMemoryIds ?? [])
        {
            if (id is not null && lower.Contains(id.ToLowerInvariant()))
                failures.Add($"forbidden-memory-id {id} leaked into response");
        }

        // Non-fabrication / privacy: literal phrases that must not appear.
        foreach (var phrase in c.ForbiddenPhrases ?? [])
        {
            if (phrase is not null && lower.Contains(phrase.ToLowerInvariant()))
                failures.Add($"forbidden phrase present: \"{phrase}\"");
        }

        // Retrieval recall: each expected memory id must be surfaced (recall leg worked).
        // The harness keys on the memory id so the check is deterministic without a model.
        foreach (var id in c.ExpectedMemoryIds ?? [])
        {
            if (id is not null && !lower.Contains(id.ToLowerInvariant()))
                failures.Add($"expected memory id not surfaced: {id}");
        }

        return failures;
    }
}
