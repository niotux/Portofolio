using System.Text.Json;
using System.Text.Json.Nodes;
using PiSharp.Core.Agent;

namespace PiSharp.Core.Notebook;

public sealed class NotebookTool(NotebookStore store, Func<string?>? taskKey = null) : IAgentTool
{
    /// <summary>The seat guideline for working memory, also carried by the system prompt when this tool is present.</summary>
    public const string MemoryGuideline = "Keep working memory in the notebook (op=write), not in .md files; repo files are for code and docs the product needs.";
    public const int MaxBatch = 50, DefaultSearchLimit = 10, MaxSearchLimit = 25;

    public string Name => "notebook";
    public string Label => Name;
    public string Description => "Exact private durable task memory. Writes are local pending sync until KB acknowledges. " +
        MemoryGuideline + " " +
        "write: upsert a note (conclusions, decisions, next steps, file/commit references) by key (id), with title, tags and text; no revision needed, last write wins, history is kept. " +
        "search: ranked full-text over title, tags and text (query words all match; \"phrase\", a or b, -word; tags= and task= filter), top 10 with snippets. " +
        "get requires id; list returns IDs and revisions. put requires id, text and expectedRevision (0 for new). " +
        "feedback: id, feedback=helpful|harmful and sourceId=<harness verification ID for this task>; evaluates a lesson against observed evidence. " +
        "transition requires id and status (expectedRevision optional: it applies to the current revision); preserves all text and provenance. " +
        "batch: ops=[{op:put|write|transition,...}] runs several in one call, each result reported. " +
        "After verification, transition resolved sources/commitments to completed. Valid statuses: open, completed, cancelled, superseded, blocked. " +
        "Never mark work complete without evidence. Sources are immutable; corrections are new records linked by sourceId.";
    public ToolExecutionMode? ExecutionMode => ToolExecutionMode.Sequential;
    public JsonObject Parameters => JsonNode.Parse("""
        {"type":"object","properties":{"op":{"type":"string","enum":["get","list","search","write","put","transition","batch","feedback"]},"id":{"type":"string","description":"Record key. Required for get/write/put/transition. Use an existing ID from list or search, or choose a new one (letters, digits, - _ . :)."},"kind":{"type":"string","enum":["source","instruction","commitment","decision","check","attempt","artifact","note","plan-step"]},"title":{"type":"string","description":"write/put: one-line title, searched and shown in the briefing index."},"tags":{"type":"array","items":{"type":"string"},"description":"write/put: labels for search. search: records must carry all of them."},"order":{"type":"number","description":"write/put on kind=plan-step: the step's position in the plan (0, 1, 2, ...). Unspecified steps sort last."},"check":{"type":"string","description":"write/put on kind=plan-step: the done-check spec the step must pass before it may be completed (max 200 chars)."},"text":{"type":"string"},"task":{"type":"string","description":"Task key the record belongs to, e.g. chat-167984 (max 160 chars). Not a description: that goes in text. search: filter."},"status":{"type":"string","enum":["open","completed","cancelled","superseded","blocked"]},"expectedRevision":{"type":"integer","minimum":0,"description":"Required for put: the current revision from get/list, or 0 for a new record. Optional for transition; not used by write."},"feedback":{"type":"string","enum":["helpful","harmful"]},"sourceId":{"type":"string"},"operationId":{"type":"string"},"query":{"type":"string","description":"search: words to rank by. list: substring filter on text."},"limit":{"type":"integer","minimum":1,"maximum":25,"description":"search: results to return (default 10)."},"ops":{"type":"array","items":{"type":"object"},"description":"batch: up to 50 operations, each an object with op (put, write or transition) and that op's fields."},"offset":{"type":"integer","minimum":0,"description":"Character offset for get; record offset for list. Follow nextOffset until null."}},"required":["op"]}
        """)!.AsObject();

    public Task<AgentToolResult> ExecuteAsync(string toolCallId, JsonObject arguments, Action<AgentToolResult>? onUpdate, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Str(arguments, "op") == "batch") return Task.FromResult(AgentToolResult.FromText(Batch(arguments, cancellationToken).ToJsonString()));
            return Task.FromResult(AgentToolResult.FromText(Run(arguments).ToJsonString()));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or FormatException or JsonException)
        { return Task.FromResult(AgentToolResult.Error(ex.Message)); }
    }

    private static string? Str(JsonObject arguments, string key) => arguments[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    /// <summary>A whole number however it arrived: a parsed JSON number, a CLR int or long (batch entries built in
    /// code), or a numeric string.</summary>
    private static long? Long(JsonNode? node) => node switch
    {
        null => null,
        JsonValue v when v.TryGetValue<long>(out var l) => l,
        JsonValue v when v.TryGetValue<int>(out var i) => i,
        JsonValue v when v.TryGetValue<string>(out var s) && long.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var p) => p,
        _ => throw new ArgumentException($"Expected a whole number, got {node.ToJsonString()}."),
    };

    /// <summary>A step order however it arrived: a parsed JSON number, a whole number, or a numeric string.</summary>
    private static double? Double(JsonNode? node) => node switch
    {
        null => null,
        JsonValue v when v.TryGetValue<double>(out var d) => d,
        JsonValue v when v.TryGetValue<long>(out var l) => l,
        JsonValue v when v.TryGetValue<int>(out var i) => i,
        JsonValue v when v.TryGetValue<string>(out var s) && double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var p) => p,
        _ => throw new ArgumentException($"Expected a number, got {node.ToJsonString()}."),
    };

    private JsonNode Run(JsonObject arguments)
    {
        string? S(string key) => Str(arguments, key);
        // "key" is what the memory ops are described with; the record field is id.
        var id = S("id") ?? S("key");
        var op = S("op");
        if (op is "put" or "transition" or "write" && id is not null && IsRuntimeEvidence(id))
            throw new ArgumentException("Runtime evidence records are read-only through notebook; create a linked note instead.");
        if (op is "transition" && id is not null && IsCheckedPlanStepCompletion(store, id, S("status")))
            throw new ArgumentException($"Step {id} has a check; use 'plan done' to verify it before completing.");
        return op switch
        {
            "get" => Read(id, (int?)Long(arguments["offset"]) ?? 0),
            "feedback" => NotebookLessons.Feedback(store, id ?? throw new ArgumentException("id required"), S("feedback") ?? "",
                taskKey?.Invoke() ?? S("task") ?? throw new ArgumentException("task required"), S("sourceId") ?? throw new ArgumentException("sourceId required: harness verification evidence")),
            "list" => ListPage(store.List(S("task"), S("kind"), S("status"), S("query")), (int?)Long(arguments["offset"]) ?? 0),
            "search" => Search(S("query") ?? throw new ArgumentException("query required: the words to search for."), Tags(arguments["tags"]), S("task"), S("kind"), S("status"),
                (int?)Long(arguments["limit"]) ?? DefaultSearchLimit),
            "write" => Write(id ?? throw new ArgumentException("id (the key) required: a stable name for this note, e.g. p4-design or wi-12-findings."),
                S("text") ?? throw new ArgumentException("text required: the conclusion to keep."), S("title"), arguments.ContainsKey("tags") ? Tags(arguments["tags"]) : null,
                S("kind"), S("task"), S("status"), S("sourceId"), Double(arguments["order"]), S("check")),
            "transition" => Transition(id ?? throw new ArgumentException("id required; use list or search to find record IDs."),
                S("status") ?? throw new ArgumentException("status required: open, completed, cancelled, superseded, blocked."),
                Long(arguments["expectedRevision"]), S("operationId")),
            "put" => Put(id ?? throw new ArgumentException("id required"), S("kind") ?? "note", S("text") ?? "", S("task") ?? "", S("status") ?? "open",
                Long(arguments["expectedRevision"]) ?? throw new ArgumentException("expectedRevision required: 0 for a new record, else the current revision from get/list (or use op=write, which needs none)."),
                S("sourceId"), S("operationId"), S("title"), arguments.ContainsKey("tags") ? Tags(arguments["tags"]) : null,
                Double(arguments["order"]), S("check")),
            "batch" => throw new ArgumentException("batch cannot nest."),
            _ => throw new ArgumentException("Unknown notebook operation. Use get, list, search, write, put, transition or batch.")
        };
    }

    /// <summary>True when a raw notebook transition would move a check-gated plan-step to completed. The gate
    /// forces the model through <c>plan done</c> so the harness runs the check before the step is marked done.</summary>
    private static bool IsCheckedPlanStepCompletion(NotebookStore store, string id, string? status)
    {
        if (status != "completed") return false;
        var record = store.Get(id);
        if (record is null || record["kind"]!.GetValue<string>() != "plan-step") return false;
        return store.PlanStepCheck(id) is { Length: > 0 };
    }

    private JsonObject Read(string? id, int offset)
    {
        var record = store.Get(id ?? "") ?? throw new ArgumentException(NotFound(id));
        if (taskKey?.Invoke() is { } key) store.RecordUsage(record["id"]!.GetValue<string>(), "used", key);
        return ReadPage(record, offset);
    }

    private static bool IsRuntimeEvidence(string id) => id.StartsWith("job-", StringComparison.Ordinal) || id.StartsWith("budget-", StringComparison.Ordinal)
        || id.StartsWith("delivery-chat-", StringComparison.Ordinal) || id.StartsWith("artifact-", StringComparison.Ordinal) || id.StartsWith("learning-", StringComparison.Ordinal)
        || id.StartsWith(PiSharp.Core.Verification.VerificationRecords.VerifyPrefix, StringComparison.Ordinal) || id == "runtime-tool-activation";

    /// <summary>Tags as an array, or as one string ("a, b" or a JSON array in a string), which models also send.</summary>
    private static string[] Tags(JsonNode? node)    {
        switch (node)
        {
            case null: return [];
            case JsonArray array: return array.Select(t => t is JsonValue v && v.TryGetValue<string>(out var s) ? s : t?.ToJsonString() ?? "").ToArray();
            case JsonValue value when value.TryGetValue<string>(out var text):
                text = text.Trim();
                if (text.StartsWith('[') && JsonNode.Parse(text) is JsonArray parsed) return Tags(parsed);
                return text.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            default: throw new ArgumentException("tags is a list of strings.");
        }
    }

    /// <summary>Several writes in one call. Each runs on its own (not a transaction: an earlier success is not rolled
    /// back by a later failure) and reports its own result, so one bad entry does not cost the others a retry.</summary>
    private JsonObject Batch(JsonObject arguments, CancellationToken cancellationToken)
    {
        var node = arguments["ops"];
        if (node is JsonValue v && v.TryGetValue<string>(out var text)) node = JsonNode.Parse(text);
        if (node is not JsonArray ops || ops.Count == 0) throw new ArgumentException("ops required: a list of {op: put|write|transition, ...} objects.");
        if (ops.Count > MaxBatch) throw new ArgumentException($"At most {MaxBatch} operations per batch; got {ops.Count}.");
        var results = new JsonArray();
        var applied = 0;
        for (var i = 0; i < ops.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = new JsonObject { ["index"] = i };
            try
            {
                if (ops[i] is not JsonObject item) throw new ArgumentException("Each batch entry is an object with op and its fields.");
                var op = Str(item, "op");
                entry["op"] = op;
                if ((Str(item, "id") ?? Str(item, "key")) is { } id) entry["id"] = id;
                if (op is not ("put" or "write" or "transition")) throw new ArgumentException("A batch entry's op is put, write or transition.");
                entry["result"] = Run(item);
                entry["ok"] = true;
                applied++;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or FormatException or JsonException)
            {
                entry["ok"] = false;
                entry["error"] = ex.Message;
            }
            results.Add((JsonNode)entry);
        }
        return new JsonObject { ["applied"] = applied, ["failed"] = ops.Count - applied, ["results"] = results };
    }

    private JsonObject Write(string id, string text, string? title, IReadOnlyList<string>? tags, string? kind, string? task, string? status, string? sourceId,
        double? order, string? check)
    {
        var (record, changed) = store.Write(id, text, title, tags, kind, task, status, sourceId, order, check);
        var result = Metadata(record);
        if (!changed) result["unchanged"] = true;
        return result;
    }

    private JsonObject Search(string query, string[] tags, string? task, string? kind, string? status, int limit)
    {
        var hits = store.Search(query, tags, task, Math.Clamp(limit, 1, MaxSearchLimit), out var mode, kind is null ? null : [kind], status);
        var result = new JsonObject
        {
            ["match"] = mode,
            ["results"] = new JsonArray(hits.Select(h =>
            {
                var row = Metadata(h.Record);
                row["score"] = h.Score;
                row["snippet"] = h.Snippet;
                return (JsonNode)row;
            }).ToArray())
        };
        if (mode == "any") result["note"] = "No record matched every word; these match some of them, best first.";
        if (hits.Count == 0) result["note"] = "No matches. Try fewer or other words, or list (task=, kind=).";
        return result;
    }

    // A transition changes the status only and keeps text and provenance, so it applies to the current revision: a
    // stale or missing expectedRevision is not a conflict worth a retry (158 of 2,215 transitions failed on one in
    // 48 h, 2026-09-27, each followed by a get and a second call). A transition to the status the record already
    // has is a no-op. Should the record change between the read and the write anyway, it is re-read and applied once
    // more: a status change is still valid against any later revision.
    private JsonObject Transition(string id, string status, long? expected, string? operationId)
    {
        for (var attempt = 0; ; attempt++)
        {
            var row = store.Get(id) ?? throw new ArgumentException(NotFound(id));
            var current = row["revision"]!.GetValue<long>();
            try
            {
                var result = Metadata(store.Transition(id, status, operationId));
                if (expected is { } e && e != current) result["note"] = $"expectedRevision {e} was stale; applied to current revision {current}.";
                if (attempt > 0) result["retried"] = true;
                return result;
            }
            catch (KeyNotFoundException) { throw new ArgumentException(NotFound(id)); }
            catch (InvalidOperationException ex) when (attempt == 0 && ex.Message.StartsWith("Notebook revision conflict", StringComparison.Ordinal)) { }
        }
    }

    // A put conflict returns what is there now, so the next call can be the right one instead of a get first. A put
    // whose only difference from the current record is the status (a transition written as a put) is still valid on
    // the current revision, so it is re-applied there once instead of failing.
    private JsonObject Put(string id, string kind, string text, string task, string status, long expected, string? sourceId, string? operationId,
        string? title, IReadOnlyList<string>? tags, double? order, string? check)
    {
        try { return Metadata(store.Put(id, kind, text, task, status, expected, sourceId, operationId, title, tags, order, check)); }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("Notebook revision conflict", StringComparison.Ordinal) && store.Get(id) is { } row)
        {
            if (expected > 0 && row["kind"]!.GetValue<string>() == kind && row["text"]!.GetValue<string>() == text && row["task"]!.GetValue<string>() == task)
            {
                var current = row["revision"]!.GetValue<long>();
                try
                {
                    var result = Metadata(store.Put(id, kind, text, task, status, current, sourceId ?? row["sourceId"]?.GetValue<string>(), operationId, title, tags, order, check));
                    result["note"] = $"expectedRevision {expected} was stale; the content was unchanged, so it was applied to current revision {current}.";
                    return result;
                }
                catch (InvalidOperationException) { }
            }
            throw new InvalidOperationException($"{ex.Message} Current record: {Metadata(row).ToJsonString()}. " +
                (expected == 0 ? "That ID is taken: put with its current revision to replace it, use op=write to upsert, or choose another ID." : "get it, merge, and put with the current revision (or use op=write)."));
        }
    }

    /// <summary>"Record not found" with the closest existing IDs: seats guess IDs such as commitment-wi-12 (130
    /// not-found errors in 48 h, 2026-09-27) and then list everything to find the real one.</summary>
    private string NotFound(string? id) => NotFoundMessage(store, id);

    internal static string NotFoundMessage(NotebookStore store, string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "id required; use list or search to find record IDs.";
        var close = ClosestIds(store, id);
        return close.Length == 0
            ? $"Record not found: {id}. No similar IDs; use search (query=) or list (task=, kind=) to find records."
            : $"Record not found: {id}. Closest IDs: {string.Join(", ", close)}.";
    }

    internal static string[] ClosestIds(NotebookStore store, string id)
    {
        var tokens = id.ToLowerInvariant().Split(['-', '_', '.', ':'], StringSplitOptions.RemoveEmptyEntries).Where(t => t.Length > 1).ToArray();
        return store.List()
            .Select(r => r["id"]!.GetValue<string>())
            .Select(c => (Id: c, Score: c.Contains(id, StringComparison.OrdinalIgnoreCase) || id.Contains(c, StringComparison.OrdinalIgnoreCase)
                ? 100 : tokens.Count(t => c.Contains(t, StringComparison.OrdinalIgnoreCase))))
            .Where(c => c.Score > 0).OrderByDescending(c => c.Score).ThenBy(c => c.Id, StringComparer.Ordinal).Take(5).Select(c => c.Id).ToArray();
    }

    private static JsonObject Metadata(JsonObject row)
    {
        var result = new JsonObject { ["id"] = row["id"]!.DeepClone(), ["revision"] = row["revision"]!.DeepClone(), ["kind"] = row["kind"]!.DeepClone(),
            ["status"] = row["status"]!.DeepClone(), ["task"] = row["task"]!.DeepClone() };
        if (row["title"] is { } title) result["title"] = title.DeepClone();
        if (row["tags"] is { } tags) result["tags"] = tags.DeepClone();
        if (row["order"] is { } order) result["order"] = order.DeepClone();
        if (row["check"] is { } check) result["check"] = check.DeepClone();
        return result;
    }

    private static JsonObject ReadPage(JsonObject row, int offset)
    {
        var text = row["text"]!.GetValue<string>(); offset = Math.Clamp(offset, 0, text.Length);
        var count = Math.Min(7000, text.Length - offset); row["text"] = text.Substring(offset, count);
        row["totalCharacters"] = text.Length; row["offset"] = offset;
        row["nextOffset"] = offset + count < text.Length ? JsonValue.Create(offset + count) : null;
        return row;
    }
    private static JsonObject ListPage(IReadOnlyList<JsonObject> rows, int offset)
    {
        offset = Math.Clamp(offset, 0, rows.Count);
        return new JsonObject { ["records"] = new JsonArray(rows.Skip(offset).Take(20).Select(r => (JsonNode)Metadata(r)).ToArray()),
            ["total"] = rows.Count, ["nextOffset"] = offset + 20 < rows.Count ? JsonValue.Create(offset + 20) : null };
    }
}
