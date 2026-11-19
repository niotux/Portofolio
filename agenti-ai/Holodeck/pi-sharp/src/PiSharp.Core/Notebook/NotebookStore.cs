using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace PiSharp.Core.Notebook;

/// <summary>Exact, append-only task memory. The journal is private state, outside resettable work/config.
/// A flush precedes adoption; an exclusive lease prevents two processes from becoming the same writer.</summary>
public sealed class NotebookStore : IDisposable
{
    private readonly FileStream _lease;
    private readonly string _journal;
    private readonly NotebookUsage _usage;
    private readonly object _gate = new();
    private readonly Dictionary<string, JsonObject> _records = new(StringComparer.Ordinal);
    private readonly Dictionary<string, JsonObject> _operations = new(StringComparer.Ordinal);
    private readonly HashSet<string> _acknowledged = new(StringComparer.Ordinal);
    /// <summary>Unacknowledged local operations per record id: whether a remote snapshot may replace a record is one lookup.
    /// It was a scan of every operation the seat ever wrote, for each record a restore imports: team-lead-2's start spent
    /// about an hour of CPU in it (2026-10-24/25, "startup: still in 'notebook open; syncing it'").</summary>
    private readonly Dictionary<string, int> _pendingRecords = new(StringComparer.Ordinal);
    private int _pendingCount;
    public string Owner { get; }
    public string DirectoryPath { get; }
    public string? RecallScope { get; set; }
    public NotebookSemanticIndex? SemanticIndex { get; set; }
    public const int MaxRecordBytes = 262144;
    public const long MaxJournalBytes = 128L * 1024 * 1024;

    public NotebookStore(string directory, string owner)
    {
        Owner = owner;
        DirectoryPath = Path.GetFullPath(directory);
        Directory.CreateDirectory(DirectoryPath);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(DirectoryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        _lease = new FileStream(Path.Combine(DirectoryPath, "writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        _journal = Path.Combine(DirectoryPath, "notebook.jsonl");
        try { Recover(); _usage = new NotebookUsage(DirectoryPath, Owner); }
        catch { _lease.Dispose(); throw; }
    }

    private void Recover()
    {
        if (!File.Exists(_journal)) return;
        var bytes = File.ReadAllBytes(_journal);
        var start = 0;
        List<(int Start, int Length)>? tornPrefixes = null;
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] != (byte)'\n') continue;
            var text = Encoding.UTF8.GetString(bytes, start, i - start);
            if (ParseRecord(text) is not { } row)
            {
                // Before the append rollback below existed, a write that stopped partway (a full disk, strata 2026-10-21)
                // left a torn record that the next append completed with its own record and newline: "<torn><record>\n".
                // That one shape is the torn tail of the past write followed by a good record: set the torn bytes aside,
                // as an incomplete final append is, and keep the record. Any other completed corruption still fails closed.
                var cut = TornPrefixLength(text);
                if (cut <= 0) throw new InvalidDataException("Invalid notebook journal record.");
                row = ParseRecord(text[cut..])!;
                (tornPrefixes ??= []).Add((start, Encoding.UTF8.GetByteCount(text[..cut])));
            }
            if (row["owner"]?.GetValue<string>() != Owner) throw new InvalidDataException("Notebook owner mismatch.");
            Apply(row);
            start = i + 1;
        }
        if (tornPrefixes is not null) start = RemoveTornPrefixes(bytes, tornPrefixes, start, out bytes);
        // Only an incomplete final append is recoverable; corruption in a completed record fails closed.
        if (start != bytes.Length)
        {
            File.WriteAllBytes(_journal + ".torn-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), bytes[start..]);
            using var stream = new FileStream(_journal, FileMode.Open, FileAccess.Write);
            stream.SetLength(start);
            stream.Flush(true);
        }
    }

    private static JsonObject? ParseRecord(string text)
    {
        try { return JsonNode.Parse(text) as JsonObject; }
        catch (System.Text.Json.JsonException) { return null; }
    }

    /// <summary>Where a whole record starts inside a line that is not one: the first "{" after which the rest of the line
    /// parses as a single record. -1 when there is none.</summary>
    private static int TornPrefixLength(string text)
    {
        for (var p = text.IndexOf('{', 1); p > 0; p = text.IndexOf('{', p + 1))
            if (text.AsSpan(p).StartsWith("{\"") && ParseRecord(text[p..]) is not null) return p;
        return -1;
    }

    /// <summary>Writes each torn prefix to its own .torn file and rewrites the journal without them (temp file, then
    /// replace), so the next start reads a clean journal. Returns the cleaned journal's offset for <paramref name="start"/>.</summary>
    private int RemoveTornPrefixes(byte[] bytes, List<(int Start, int Length)> prefixes, int start, out byte[] cleaned)
    {
        var stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        using var kept = new MemoryStream(bytes.Length);
        var from = 0;
        var removedBeforeStart = 0;
        for (var n = 0; n < prefixes.Count; n++)
        {
            var (at, length) = prefixes[n];
            File.WriteAllBytes($"{_journal}.torn-{stamp}-{n + 1}", bytes[at..(at + length)]);
            kept.Write(bytes, from, at - from);
            from = at + length;
            if (at < start) removedBeforeStart += length;
        }
        kept.Write(bytes, from, bytes.Length - from);
        cleaned = kept.ToArray();
        var temp = _journal + ".repair";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write))
        {
            stream.Write(cleaned);
            stream.Flush(true);
        }
        File.Move(temp, _journal, overwrite: true);
        return start - removedBeforeStart;
    }

    private void Append(JsonObject row)
    {
        var bytes = Encoding.UTF8.GetBytes(row.ToJsonString() + "\n");
        using var stream = new FileStream(_journal, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
        if (stream.Length + bytes.Length > MaxJournalBytes) throw new IOException("Notebook journal quota reached; export/archive before continuing.");
        var length = stream.Seek(0, SeekOrigin.End);
        try
        {
            stream.Write(bytes);
            stream.Flush(true);
        }
        catch (IOException)
        {
            // A write that stopped partway (a full disk, 2026-10-21 on strata) would leave a torn record that the next
            // append completes with its newline, and Recover rightly refuses a completed corrupt record: the seat then
            // fails at every start. Put the journal back the way it was before failing the append.
            try { stream.SetLength(length); stream.Flush(true); } catch (IOException) { }
            throw;
        }
        Apply(row);
    }

    /// <summary><see cref="Append"/> for several rows: one write and one flush to disk, all or none.</summary>
    private void AppendAll(IReadOnlyList<JsonObject> rows)
    {
        var bytes = Encoding.UTF8.GetBytes(string.Concat(rows.Select(r => r.ToJsonString() + "\n")));
        using var stream = new FileStream(_journal, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
        if (stream.Length + bytes.Length > MaxJournalBytes) throw new IOException("Notebook journal quota reached; export/archive before continuing.");
        var length = stream.Seek(0, SeekOrigin.End);
        try
        {
            stream.Write(bytes);
            stream.Flush(true);
        }
        catch (IOException)
        {
            try { stream.SetLength(length); stream.Flush(true); } catch (IOException) { }
            throw;
        }
        foreach (var row in rows) Apply(row);
    }

    private void Apply(JsonObject row)
    {
        if (row["version"] is { } version && version.GetValue<int>() != 1) throw new InvalidDataException("Unsupported notebook journal version.");
        var operation = row["operationId"]!.GetValue<string>();
        if (row["ack"]?.GetValue<bool>() == true)
        {
            if (!_operations.TryGetValue(operation, out var acked)) throw new InvalidDataException("Receipt precedes its notebook operation.");
            if (_acknowledged.Add(operation))
            {
                _pendingCount--;
                var ackedId = acked["record"]!["id"]!.GetValue<string>();
                if (_pendingRecords.TryGetValue(ackedId, out var left) && --left > 0) _pendingRecords[ackedId] = left;
                else _pendingRecords.Remove(ackedId);
            }
            return;
        }
        var record = (JsonObject)row["record"]!;
        if (row["imported"]?.GetValue<bool>() != true)
        {
            var canonical = new JsonObject();
            foreach (var key in new[] { "id", "kind", "text", "task", "status", "sourceId", "revision" }) canonical[key] = record[key]?.DeepClone();
            if (record["order"] is not null) canonical["order"] = record["order"]!.DeepClone();
            if (record["check"] is not null) canonical["check"] = record["check"]!.DeepClone();
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToJsonString())));
            if (hash != row["payloadHash"]?.GetValue<string>()) throw new InvalidDataException("Notebook payload checksum mismatch.");
            if (_operations.TryGetValue(operation, out var previous))
            {
                if (hash != previous["payloadHash"]?.GetValue<string>()) throw new InvalidDataException("Conflicting notebook operation replay.");
                return;
            }
        }
        _records[record["id"]!.GetValue<string>()] = (JsonObject)record.DeepClone();
        if (row["imported"]?.GetValue<bool>() == true) return;
        if (!_operations.ContainsKey(operation))
        {
            _pendingCount++;
            var recordId = record["id"]!.GetValue<string>();
            _pendingRecords[recordId] = _pendingRecords.GetValueOrDefault(recordId) + 1;
        }
        _operations[operation] = (JsonObject)row.DeepClone();
    }

    public const int MaxTitleLength = 200, MaxTags = 16, MaxTagLength = 40;
    public const int MaxCheckLength = 200;

    /// <summary>Tags are lower-cased, trimmed, de-duplicated and kept in the order given.</summary>
    public static string[] NormalizeTags(IEnumerable<string>? tags)
    {
        var result = new List<string>();
        foreach (var raw in tags ?? [])
        {
            var tag = (raw ?? "").Trim().ToLowerInvariant();
            if (tag.Length == 0 || result.Contains(tag)) continue;
            if (tag.Length > MaxTagLength) throw new ArgumentException($"A tag is a short label (max {MaxTagLength} chars); \"{tag[..20]}...\" is {tag.Length}.");
            result.Add(tag);
        }
        if (result.Count > MaxTags) throw new ArgumentException($"At most {MaxTags} tags per record; got {result.Count}.");
        return [.. result];
    }

    private static string? TitleOf(JsonObject? record) => record?["title"]?.GetValue<string>();
    private static string[] TagsOf(JsonObject? record) => (record?["tags"] as JsonArray)?.Select(t => t!.GetValue<string>()).ToArray() ?? [];
    private static string? CheckOf(JsonObject? record) => record?["check"]?.GetValue<string>();
    /// <summary>The plan-step order; absent or null means "unspecified", and sorts after every specified order.</summary>
    private static double? OrderOf(JsonObject? record) => record?["order"] is JsonValue v ? v.GetValue<double>() : null;
    /// <summary>Plan steps run in ascending order, then id for ties and unspecified orders (they sort last).</summary>
    internal static readonly IComparer<JsonObject> PlanStepOrder = Comparer<JsonObject>.Create((a, b) =>
    {
        var oa = OrderOf(a) ?? double.MaxValue;
        var ob = OrderOf(b) ?? double.MaxValue;
        return oa != ob ? oa.CompareTo(ob) : string.CompareOrdinal(a["id"]!.GetValue<string>(), b["id"]!.GetValue<string>());
    });

    /// <param name="tags">Search labels; null keeps the record's current ones. Title and tags are outside the synced
    /// payload hash (the KB stores the record body as jsonb, so they travel with it unchanged).</param>
    /// <param name="order">Plan-step position; null keeps the current value. Only meaningful on <c>plan-step</c> records.</param>
    /// <param name="check">The done-check spec a step must pass before it may be completed; null keeps the current value.</param>
    public JsonObject Put(string id, string kind, string text, string task, string status, long expectedRevision,
        string? sourceId = null, string? operationId = null, string? title = null, IReadOnlyList<string>? tags = null,
        double? order = null, string? check = null)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 160 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.' or ':')))
            throw new ArgumentException("Notebook IDs use ASCII letters, digits, dash, underscore, dot or colon (max 160).");
        // Its own message: team-lead-2 (2026-09-25) wrote a sentence into task, was told its ID was wrong, and renamed
        // the ID thirteen times until the repeat guard stopped it.
        if (task.Length > 160)
            throw new ArgumentException($"task is a task key such as chat-167984 (max 160 chars), not a description; yours is {task.Length} chars. Put the description in text.");
        if (expectedRevision < 0 || expectedRevision == long.MaxValue) throw new ArgumentException("Invalid notebook revision.");
        if (kind is not ("source" or "instruction" or "commitment" or "decision" or "check" or "attempt" or "artifact" or "note" or "plan-step" or "guard" or "guard-call" or "correction"))
            throw new ArgumentException("Unknown notebook kind. Use source, instruction, commitment, decision, check, attempt, artifact, note, plan-step, guard, guard-call or correction.");
        if (check is not null && check.Trim().Length > MaxCheckLength) throw new ArgumentException($"A check spec is one line (max {MaxCheckLength} chars).", nameof(check));
        if (status is not ("open" or "completed" or "cancelled" or "superseded" or "blocked")) throw new ArgumentException("Invalid status. Use open, completed, cancelled, superseded or blocked.");
        if (Encoding.UTF8.GetByteCount(text) > MaxRecordBytes) throw new ArgumentException("Notebook record exceeds 256 KiB; store an artifact reference.");
        if (title is not null && title.Trim().Length > MaxTitleLength) throw new ArgumentException($"A title is one line (max {MaxTitleLength} chars); put the rest in text.");
        if (order is not null && (!double.IsFinite(order.Value) || order.Value < 0)) throw new ArgumentException("A plan-step order is a finite non-negative number.", nameof(order));
        var normalizedTags = tags is null ? null : NormalizeTags(tags);
        operationId ??= Guid.NewGuid().ToString("N");
        if (operationId.Length is < 1 or > 160) throw new ArgumentException("Invalid operation ID.");
        lock (_gate)
        {
            // order and check ARE in the idempotency comparison (unlike title/tags): they change what a step means.
            var revision = _records.TryGetValue(id, out var old) ? old["revision"]!.GetValue<long>() : 0;
            var finalOrder = order ?? OrderOf(old);
            var finalCheck = check is null ? CheckOf(old) : check.Trim();
            var canonical = new JsonObject { ["id"] = id, ["kind"] = kind, ["text"] = text, ["task"] = task,
                ["status"] = status, ["sourceId"] = sourceId, ["revision"] = expectedRevision + 1 };
            if (finalOrder is not null) canonical["order"] = finalOrder.Value;
            if (!string.IsNullOrEmpty(finalCheck)) canonical["check"] = finalCheck;
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToJsonString())));
            if (_operations.TryGetValue(operationId, out var prior))
            {
                var priorRecord = (JsonObject)prior["record"]!;
                if (prior["payloadHash"]!.GetValue<string>() != hash
                    || (title is not null && title.Trim() != (TitleOf(priorRecord) ?? ""))
                    || (normalizedTags is not null && !normalizedTags.SequenceEqual(TagsOf(priorRecord))))
                    throw new InvalidOperationException("Operation ID reused with a different payload.");
                return (JsonObject)priorRecord.DeepClone();
            }
            var finalTitle = title is null ? TitleOf(old) : title.Trim();
            var finalTags = normalizedTags ?? TagsOf(old);
            var payload = new JsonObject { ["id"] = id, ["kind"] = kind, ["text"] = text, ["task"] = task,
                ["status"] = status, ["sourceId"] = sourceId, ["revision"] = expectedRevision + 1 };
            if (!string.IsNullOrEmpty(finalTitle)) payload["title"] = finalTitle;
            if (finalTags.Length > 0) payload["tags"] = new JsonArray([.. finalTags.Select(t => (JsonNode)t)]);
            if (revision != expectedRevision) throw new InvalidOperationException($"Notebook revision conflict: expected {expectedRevision}, current {revision}.");
            if (old?["kind"]?.GetValue<string>() == "source" && (kind != "source" || old["text"]!.GetValue<string>() != text))
                throw new InvalidOperationException("Source text is immutable; append an amendment with sourceId.");
            if (sourceId is not null && !_records.ContainsKey(sourceId)) throw new ArgumentException("Unknown source ID.");
            if (finalOrder is not null) payload["order"] = finalOrder.Value;
            if (!string.IsNullOrEmpty(finalCheck)) payload["check"] = finalCheck;
            payload["updatedAt"] = DateTimeOffset.UtcNow.ToString("O");
            Append(new JsonObject { ["version"] = 1, ["owner"] = Owner, ["operationId"] = operationId,
                ["payloadHash"] = hash, ["expectedRevision"] = expectedRevision, ["record"] = payload });
            return (JsonObject)payload.DeepClone();
        }
    }

    /// <summary>Upserts working memory: no revision needed, the last writer wins, and every earlier version stays in the
    /// journal (and in the KB's receipts once synced). A write that changes nothing appends nothing, so re-running an
    /// import or re-saving the same conclusion is free. Omitted fields keep their current values. Sources are exact
    /// copies of what was said and cannot be written over.</summary>
    /// <returns>The record, and whether anything was appended.</returns>
    public (JsonObject Record, bool Changed) Write(string id, string text, string? title = null, IReadOnlyList<string>? tags = null,
        string? kind = null, string? task = null, string? status = null, string? sourceId = null,
        double? order = null, string? check = null)
    {
        if (kind == "source") throw new ArgumentException("Sources are captured by the harness; write notes, decisions, checks or commitments.");
        lock (_gate)
        {
            _records.TryGetValue(id, out var old);
            if (old?["kind"]?.GetValue<string>() == "source")
                throw new InvalidOperationException($"{id} is a source (an exact copy of a message) and cannot be overwritten; write a note with sourceId={id}.");
            var finalKind = kind ?? old?["kind"]?.GetValue<string>() ?? "note";
            var finalTask = task ?? old?["task"]?.GetValue<string>() ?? "";
            var finalStatus = status ?? old?["status"]?.GetValue<string>() ?? "open";
            var finalSource = sourceId ?? old?["sourceId"]?.GetValue<string>();
            var oldOrder = OrderOf(old); var oldCheck = CheckOf(old);
            if (old is not null
                && old["kind"]!.GetValue<string>() == finalKind && old["text"]!.GetValue<string>() == text
                && old["task"]!.GetValue<string>() == finalTask && old["status"]!.GetValue<string>() == finalStatus
                && old["sourceId"]?.GetValue<string>() == finalSource
                && (title is null || title.Trim() == (TitleOf(old) ?? ""))
                && (tags is null || NormalizeTags(tags).SequenceEqual(TagsOf(old)))
                && (order is null || order.Value == (oldOrder ?? double.NaN))
                && (check is null || check.Trim() == (oldCheck ?? "")))
                return ((JsonObject)old.DeepClone(), false);
            var revision = old?["revision"]!.GetValue<long>() ?? 0;
            return (Put(id, finalKind, text, finalTask, finalStatus, revision, finalSource, title: title, tags: tags, order: order, check: check), true);
        }
    }

    /// <summary>Changes only the status, against the record's current revision, atomically. Same status: no-op.</summary>
    /// <exception cref="KeyNotFoundException">No such record.</exception>
    public JsonObject Transition(string id, string status, string? operationId = null)
    {
        lock (_gate)
        {
            if (!_records.TryGetValue(id, out var current)) throw new KeyNotFoundException(id);
            if (current["status"]!.GetValue<string>() == status) return (JsonObject)current.DeepClone();
            return Put(id, current["kind"]!.GetValue<string>(), current["text"]!.GetValue<string>(), current["task"]!.GetValue<string>(),
                status, current["revision"]!.GetValue<long>(), current["sourceId"]?.GetValue<string>(), operationId,
                title: TitleOf(current), tags: TagsOf(current), order: OrderOf(current), check: CheckOf(current));
        }
    }

    /// <summary>Atomically replaces a task's open plan snapshot. Removed open steps are superseded, never deleted. A finished
    /// step keeps done unless the plan names it pending or in_progress, which reopens it: a plan changes at any step (rule
    /// how-we-work), and pi14 (2026-10-29 13:12Z) was refused "Completed plan step s1 cannot be reopened" on a replan.
    /// An empty status is the plan saying nothing about the step.</summary>
    public IReadOnlyList<JsonObject> SetPlan(string task, IReadOnlyList<(string Id, string Text, string Title, string Status, double Order)> steps)
    {
        if (steps.Count > 20) throw new ArgumentException("A plan is limited to 20 steps; split into subplans.");
        lock (_gate)
        {
            var ids = steps.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
            var rows = new List<JsonObject>();
            foreach (var old in _records.Values.Where(r => r["kind"]!.GetValue<string>() == "plan-step"
                         && r["task"]!.GetValue<string>() == task && r["status"]!.GetValue<string>() == "open"
                         && !ids.Contains(r["id"]!.GetValue<string>())).ToArray())
                rows.Add(Transition(old["id"]!.GetValue<string>(), "superseded"));
            foreach (var step in steps)
            {
                var old = Get(step.Id);
                var status = step.Status switch
                {
                    "in_progress" or "pending" => "open",
                    "done" => "completed",
                    "" => old?["status"]!.GetValue<string>() == "completed" ? "completed" : "open",
                    _ => step.Status,
                };
                rows.Add(Write(step.Id, step.Text, title: step.Title, kind: "plan-step", task: task, status: status, order: step.Order).Record);
            }
            return rows;
        }
    }

    /// <summary>Completes the specified plan step, or the lowest-order open step for a task.</summary>
    public JsonObject CompletePlanStep(string task, string? id = null)
    {
        lock (_gate)
        {
            var candidate = ResolveStep(task, id);
            return Transition(candidate["id"]!.GetValue<string>(), "completed");
        }
    }

    /// <summary>Completes the specified plan step with verification evidence. The <paramref name="sourceId"/>
    /// links the step to the verify- record that proved it. Used by the check-gated path so the evidence is
    /// recorded on the step itself.</summary>
    public JsonObject CompletePlanStepWithEvidence(string task, string? id, string sourceId)
    {
        lock (_gate)
        {
            var candidate = ResolveStep(task, id);
            return Put(candidate["id"]!.GetValue<string>(), candidate["kind"]!.GetValue<string>(),
                candidate["text"]!.GetValue<string>(), task, "completed",
                candidate["revision"]!.GetValue<long>(), sourceId: sourceId,
                title: TitleOf(candidate), tags: TagsOf(candidate), order: OrderOf(candidate), check: CheckOf(candidate));
        }
    }

    /// <summary>The check spec on a plan-step, or null. Exposed so the PlanTool and the raw-notebook gate can
    /// decide whether a step is check-gated without re-reading the record.</summary>
    public string? PlanStepCheck(string id) { lock (_gate) return CheckOf(_records.GetValueOrDefault(id)); }

    private JsonObject ResolveStep(string task, string? id)
    {
        var candidate = id is null
            ? _records.Values.Where(r => r["kind"]!.GetValue<string>() == "plan-step" && r["task"]!.GetValue<string>() == task
                && r["status"]!.GetValue<string>() == "open").OrderBy(r => OrderOf(r) ?? double.MaxValue).ThenBy(r => r["id"]!.GetValue<string>(), StringComparer.Ordinal).FirstOrDefault()
            : _records.GetValueOrDefault(id);
        if (candidate is null || candidate["kind"]!.GetValue<string>() != "plan-step" || candidate["task"]!.GetValue<string>() != task)
            throw new KeyNotFoundException(id ?? $"open plan step for {task}");
        if (candidate["status"]!.GetValue<string>() != "open") throw new InvalidOperationException("Only an open plan step can be marked done.");
        return candidate;
    }

    /// <summary>Blocks the named plan step, or without an id the step <see cref="CompletePlanStep"/> would complete.</summary>
    public JsonObject BlockPlanStep(string task, string? id = null)
    {
        lock (_gate)
        {
            id ??= ResolveStep(task, null)["id"]!.GetValue<string>();
            if (!_records.TryGetValue(id, out var row) || row["kind"]!.GetValue<string>() != "plan-step" || row["task"]!.GetValue<string>() != task)
                throw new KeyNotFoundException(id);
            if (row["status"]!.GetValue<string>() != "open") throw new InvalidOperationException("Only an open plan step can be blocked.");
            return Transition(id, "blocked");
        }
    }

    private readonly NotebookSearchIndex _search = new();

    public IReadOnlyList<JsonObject> RecallCandidates() => List().Where(r =>
        r["kind"]!.GetValue<string>() is "note" or "decision"
        && r["status"]!.GetValue<string>() is "open" or "completed" or "blocked"
        && !IsArchived(r["id"]!.GetValue<string>())
        && (RecallScope is null || !TagsOf(r).Any(t => t.StartsWith("repo:", StringComparison.Ordinal)) || TagsOf(r).Contains(RecallScope)))
        .OrderByDescending(r => r["updatedAt"]!.GetValue<string>(), StringComparer.Ordinal).ToArray();

    public Task PrepareRecallAsync(string query, CancellationToken ct) => SemanticIndex?.PrepareAsync(RecallCandidates(), query, ct) ?? Task.CompletedTask;

    public IReadOnlyList<NotebookHit> RecallSearch(string query, string lexicalQuery, int limit, ISet<string>? exclude = null)
    {
        var records = RecallCandidates().Where(r => exclude?.Contains(r["id"]!.GetValue<string>()) != true
            && exclude?.Contains(r["id"]!.GetValue<string>() + "@" + r["revision"]!.GetValue<long>()) != true).ToArray();
        IReadOnlyList<NotebookHit> lexical;
        lock (_gate) lexical = _search.Search(records, lexicalQuery, limit, out _);
        var semantic = SemanticIndex?.Search(records, query, limit) ?? [];
        var combined = new Dictionary<string, NotebookHit>(StringComparer.Ordinal);
        void Merge(IReadOnlyList<NotebookHit> hits)
        {
            for (var rank = 0; rank < hits.Count; rank++)
            {
                var hit = hits[rank]; var id = hit.Record["id"]!.GetValue<string>();
                var score = 1.0 / (10 + rank);
                combined[id] = hit with { Score = score + (combined.TryGetValue(id, out var previous) ? previous.Score : 0) };
            }
        }
        Merge(lexical); Merge(semantic);
        return combined.Values.OrderByDescending(h => h.Score).ThenBy(h => h.Record["id"]!.GetValue<string>(), StringComparer.Ordinal).Take(limit).ToArray();
    }

    /// <summary>Ranked full-text search over title, tags, id and text (see <see cref="NotebookSearchIndex"/>).
    /// <paramref name="tags"/> must all be present; <paramref name="task"/>, <paramref name="kinds"/> and
    /// <paramref name="status"/> filter exactly.</summary>
    public IReadOnlyList<NotebookHit> Search(string query, IReadOnlyList<string>? tags, string? task, int limit, out string mode,
        IReadOnlyCollection<string>? kinds = null, string? status = null, IReadOnlyCollection<string>? excludeIds = null,
        bool includeArchived = true)
    {
        var wanted = NormalizeTags(tags);
        lock (_gate)
        {
            var records = _records.Values.Where(r => (task is null || r["task"]!.GetValue<string>() == task)
                && (kinds is null || kinds.Contains(r["kind"]!.GetValue<string>()))
                && (status is null || r["status"]!.GetValue<string>() == status)
                && (includeArchived || !_usage.IsArchived(r["id"]!.GetValue<string>()))
                && (excludeIds is null || !excludeIds.Contains(r["id"]!.GetValue<string>()))
                && (wanted.Length == 0 || wanted.All(TagsOf(r).Contains)));
            var hits = _search.Search(records, query, limit, out mode);
            if (includeArchived)
                foreach (var hit in hits) _usage.SetArchived(hit.Record["id"]!.GetValue<string>(), false);
            return hits;
        }
    }

    /// <summary>Archive unused old notes/decisions without mutating their synced payload. Obligations and runtime
    /// evidence are excluded by kind; an item with harmful feedback wins regardless of age.</summary>
    public IReadOnlyList<string> ArchiveUnused(DateTimeOffset now, int archiveAfterDays = 14)
    {
        if (archiveAfterDays < 0) throw new ArgumentOutOfRangeException(nameof(archiveAfterDays));
        var archived = new List<string>();
        lock (_gate)
        {
            foreach (var record in _records.Values)
            {
                var recordTags = TagsOf(record);
                if (record["kind"]?.GetValue<string>() is not ("note" or "decision")
                    || recordTags.Any(tag => tag is "guard" or "guard-rule" or "guard_rule" or "runtime-evidence" or "runtime_evidence")) continue;
                var id = record["id"]!.GetValue<string>();
                if (_usage.IsArchived(id)) continue;
                var usage = _usage.Get(id);
                var updated = DateTimeOffset.TryParse(record["updatedAt"]?.GetValue<string>(), out var at) ? at : now;
                var lastUsed = usage.LastUsedAt ?? updated;
                if (usage.Harmful <= usage.Helpful
                    && (usage.UsedCount != 0 || usage.Helpful > 0 || now - lastUsed < TimeSpan.FromDays(archiveAfterDays))) continue;
                _usage.SetArchived(id, true);
                if (_usage.IsArchived(id)) archived.Add(id);
            }
        }
        return archived;
    }

    /// <summary>Apply a pairwise consolidation proposed during an idle turn. The caller supplies the bounded
    /// merge text; this method enforces same-tag/high-overlap pairing, size and identity preservation before it
    /// writes the replacement and supersedes both originals.</summary>
    public JsonObject? ConsolidatePair(string firstId, string secondId, string mergedId, string mergedText, string? title = null)
    {
        lock (_gate)
        {
            if (firstId == secondId || !_records.TryGetValue(firstId, out var first) || !_records.TryGetValue(secondId, out var second)) return null;
            var kind = first["kind"]!.GetValue<string>();
            if (kind is not ("note" or "decision") || second["kind"]!.GetValue<string>() is not ("note" or "decision")
                || first["status"]!.GetValue<string>() != "open" || second["status"]!.GetValue<string>() != "open"
                || !TagsOf(first).SequenceEqual(TagsOf(second)) || _usage.IsArchived(firstId) || _usage.IsArchived(secondId))
                return null;
            var left = first["text"]!.GetValue<string>();
            var right = second["text"]!.GetValue<string>();
            var leftWords = NotebookSearchIndex.Tokenize(left).ToHashSet(StringComparer.Ordinal);
            var rightWords = NotebookSearchIndex.Tokenize(right).ToHashSet(StringComparer.Ordinal);
            var union = leftWords.Union(rightWords).Count();
            var similarity = union == 0 ? 0 : (double)leftWords.Intersect(rightWords).Count() / union;
            if (similarity <= 0.8 || mergedText.Length > left.Length + right.Length) return null;
            var identities = System.Text.RegularExpressions.Regex.Matches(left + "\\n" + right, @"(?i)\b[0-9a-f]{7,64}\b")
                .Cast<System.Text.RegularExpressions.Match>().Select(m => m.Value).Distinct(StringComparer.OrdinalIgnoreCase);
            if (identities.Any(identity => !mergedText.Contains(identity, StringComparison.OrdinalIgnoreCase))) return null;
            if (mergedId == firstId || mergedId == secondId || string.IsNullOrWhiteSpace(mergedId)) return null;
            var created = Write(mergedId, mergedText, title, TagsOf(first), kind, first["task"]!.GetValue<string>(), "open").Record;
            Write(firstId, left, kind: kind, task: first["task"]!.GetValue<string>(), status: "superseded",
                sourceId: first["sourceId"]?.GetValue<string>(), title: TitleOf(first), tags: TagsOf(first));
            Write(secondId, right, kind: second["kind"]!.GetValue<string>(), task: second["task"]!.GetValue<string>(), status: "superseded",
                sourceId: second["sourceId"]?.GetValue<string>(), title: TitleOf(second), tags: TagsOf(second));
            return created;
        }
    }

    public bool IsArchived(string id) => _usage.IsArchived(id);
    public void SetArchived(string id, bool archived) => _usage.SetArchived(id, archived);
    public NotebookUsage.Counter Usage(string id) => _usage.Get(id);
    public void RecordUsage(string id, string eventName, string taskKey) => _usage.Record(id, eventName, taskKey);
    public bool TryRecordUsage(string id, string eventName, string taskKey, string evidenceId) => _usage.Record(id, eventName, taskKey, evidenceId);

    public JsonObject? Get(string id) { lock (_gate) return _records.TryGetValue(id, out var value) ? (JsonObject)value.DeepClone() : null; }
    public IReadOnlyList<JsonObject> List(string? task = null, string? kind = null, string? status = null, string? query = null)
    {
        lock (_gate) return _records.Values.Where(r => (task is null || r["task"]!.GetValue<string>() == task)
            && (kind is null || r["kind"]!.GetValue<string>() == kind) && (status is null || r["status"]!.GetValue<string>() == status)
            && (query is null || r["text"]!.GetValue<string>().Contains(query, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(r => r["id"]!.GetValue<string>(), StringComparer.Ordinal).Select(r => (JsonObject)r.DeepClone()).ToArray();
    }
    public IReadOnlyList<JsonObject> Pending(int limit = int.MaxValue)
    {
        if (limit < 0) throw new ArgumentOutOfRangeException(nameof(limit));
        lock (_gate) return _operations.Where(p => !_acknowledged.Contains(p.Key)).Take(limit)
            .Select(p => (JsonObject)p.Value.DeepClone()).ToArray();
    }
    public int PendingCount { get { lock (_gate) return _pendingCount; } }
    public void ImportCommitted(JsonObject record) => ImportCommitted([record]);

    /// <summary>Takes committed records from the notebook server (a restore page): a record the seat already holds at that
    /// revision or later is skipped, one with local operations not yet acknowledged is refused, and the rest are written to
    /// the journal together, with one flush to disk.</summary>
    public void ImportCommitted(IReadOnlyList<JsonObject> records)
    {
        lock (_gate)
        {
            var rows = new List<JsonObject>();
            foreach (var record in records)
            {
                var id = record["id"]!.GetValue<string>();
                if (_records.TryGetValue(id, out var prior) && prior["revision"]!.GetValue<long>() >= record["revision"]!.GetValue<long>()) continue;
                if (_pendingRecords.ContainsKey(id))
                    throw new InvalidOperationException("Cannot replace pending local state with a remote snapshot.");
                rows.Add(new JsonObject { ["version"] = 1, ["owner"] = Owner, ["operationId"] = "import-" + Guid.NewGuid().ToString("N"), ["imported"] = true, ["record"] = record.DeepClone() });
            }
            if (rows.Count > 0) AppendAll(rows);
        }
    }
    public void Acknowledge(string operationId)
    {
        lock (_gate)
        {
            if (!_operations.ContainsKey(operationId)) throw new ArgumentException("Unknown operation.");
            if (!_acknowledged.Contains(operationId)) Append(new JsonObject { ["owner"] = Owner, ["operationId"] = operationId, ["ack"] = true });
        }
    }

    /// <summary>One line of the briefing, keyed so a caller can tell which lines changed between two briefings.</summary>
    public sealed record BriefingEntry(string Key, string Line);

    public const string BriefingHeader = "Durable notebook (exact records; use notebook get for full text).\n";
    /// <summary>Open notes and decisions listed by title in the briefing; the rest are found with op=search.</summary>
    public const int BriefingNotes = 8;
    /// <summary>Open sources listed in the briefing; older ones are counted, not listed.</summary>
    public const int BriefingSources = 10;
    /// <summary>The plan group gets its own line budget, ahead of everything else (the working plan must survive a 12k briefing).</summary>
    public const int PlanBudget = 1200;
    /// <summary>The plan group lists the current step, these next steps, and every blocked step; the rest are counted.</summary>
    public const int PlanNext = 2;

    public string Briefing(int maxCharacters = 12000) => maxCharacters < BriefingHeader.Length ? ""
        : BriefingHeader + string.Concat(BriefingEntries(maxCharacters).Select(e => e.Line));

    /// <summary>The briefing's lines, in order, after <see cref="BriefingHeader"/>. Four groups, in priority order:
    /// the working plan (plan-steps in order: current step detailed, the next two by title, completed as a count,
    /// blocked always listed; its own <see cref="PlanBudget"/>), then obligations (open instructions, commitments and
    /// checks, and anything blocked - plan-steps are not obligations), then an index of the newest open
    /// notes and decisions (title only; the seat's working memory, searchable), then the newest open sources.
    /// <para>The content depends only on the records, never on sync state or the clock, so an unchanged notebook gives a
    /// byte-identical briefing (what lets <see cref="NotebookContext"/> skip re-sending it).</para></summary>
    public IReadOnlyList<BriefingEntry> BriefingEntries(int maxCharacters = 12000)
    {
        var open = List().Where(r => r["status"]!.GetValue<string>() is "open" or "blocked").ToArray();
        static string Kind(JsonObject r) => r["kind"]!.GetValue<string>();
        static bool Blocked(JsonObject r) => r["status"]!.GetValue<string>() == "blocked";
        // The done count spans every plan-step of the task (completed included); the listed lines only cover open ones.
        var planSteps = List(kind: "plan-step", task: null).ToArray();
        IEnumerable<JsonObject> Newest(IEnumerable<JsonObject> rows) => rows
            .OrderByDescending(r => r["updatedAt"]?.GetValue<string>() ?? "", StringComparer.Ordinal)
            .ThenByDescending(r => r["id"]!.GetValue<string>(), StringComparer.Ordinal);
        // Plan-steps are their own group and are NOT obligations: they are the working plan, and listing them as
        // obligations too would burn the obligations budget on lines that change on every step.
        var plans = open.Where(r => Kind(r) == "plan-step").ToArray();
        var obligations = open.Where(r => Kind(r) is "instruction" or "commitment" or "check" || (Blocked(r) && Kind(r) != "plan-step")).ToArray();
        var notes = open.Where(r => Kind(r) is "note" or "decision" && !Blocked(r)
            && !_usage.IsArchived(r["id"]!.GetValue<string>())).ToArray();
        var sources = open.Where(r => Kind(r) == "source" && !Blocked(r)).ToArray();
        // Over budget, the oldest open sources give way first; notes next; obligations last. Never silently: the
        // omission is counted and says how to list them. Throwing here aborted the seat (agent-maker-2 crash loop
        // 2026-09-25: 213 open sources, never completed, one per wake).
        const int omissionLine = 120, notesLine = 110;
        var referenceBudget = maxCharacters - BriefingHeader.Length - omissionLine - (notes.Length > 0 ? notesLine : 0);
        var kept = new HashSet<JsonObject>();
        var noteLines = new Dictionary<JsonObject, string>();
        var full = false;
        bool Take(JsonObject row, int cost)
        {
            if (full || cost > referenceBudget) { full = true; return false; }
            referenceBudget -= cost;
            kept.Add(row);
            return true;
        }
        foreach (var row in Newest(obligations)) Take(row, row["id"]!.GetValue<string>().Length + 95);
        foreach (var row in Newest(notes).Take(BriefingNotes))
        {
            var label = TitleOf(row) ?? Lead(row["text"]!.GetValue<string>(), 80);
            var line = $"{row["id"]} r{row["revision"]} [{Kind(row)}] {label}\n";
            if (Take(row, line.Length)) noteLines[row] = line;
        }
        foreach (var row in Newest(sources).Take(BriefingSources)) Take(row, row["id"]!.GetValue<string>().Length + 95);

        // The plan group comes first and owns its own budget (PlanBudget): the working plan must be visible even when
        // the rest of the briefing is under pressure. Entries are keyed plan:<id> / plan:~summary so a change to one
        // step's status is a delta of exactly that key.
        var planEntries = new List<BriefingEntry>();
        var planBudget = PlanBudget;
        void PlanLine(string key, string line, bool shrink)
        {
            if (line.Length > planBudget)
            {
                line = shrink ? TrimPlanLine(line, planBudget) : line;
                if (line.Length > planBudget) return;
            }
            planBudget -= line.Length;
            planEntries.Add(new BriefingEntry(key, line));
        }
        void PlanDetailed(JsonObject row, bool markCurrent)
        {
            var order = OrderOf(row) is { } o ? $" #{o.ToString(System.Globalization.CultureInfo.InvariantCulture)}" : "";
            var check = CheckOf(row) is { } c && c.Length > 0 ? $" check: {Lead(c, 60)}" : "";
            var prefix = markCurrent ? "CURRENT" : "";
            PlanLine("plan:" + row["id"]!.GetValue<string>(),
                $"{prefix} plan{order} r{row["revision"]} [{row["status"]}] {row["text"]}{check}\n", shrink: true);
        }
        void PlanTitle(JsonObject row, string prefix)
        {
            var order = OrderOf(row) is { } o ? $" #{o.ToString(System.Globalization.CultureInfo.InvariantCulture)}" : "";
            var label = TitleOf(row) ?? Lead(row["text"]!.GetValue<string>(), 80);
            PlanLine("plan:" + row["id"]!.GetValue<string>(),
                $"{prefix} plan{order} r{row["revision"]} [{row["status"]}] {label}\n", shrink: true);
        }
        var planTotal = planSteps.Length;
        var planDone = planSteps.Count(r => r["status"]!.GetValue<string>() == "completed");
        var uncheckedDone = planSteps.Count(r => r["status"]!.GetValue<string>() == "completed" && CheckOf(r) is not { Length: > 0 });
        var openSteps = plans.Where(r => r["status"]!.GetValue<string>() != "completed").OrderBy(r => r, PlanStepOrder).ToArray();
        var blockedSteps = plans.Where(r => Blocked(r)).OrderBy(r => r, PlanStepOrder).ToArray();
        if (planTotal > 0)
        {
            var doneLine = uncheckedDone > 0
                ? FormattableString.Invariant($"plan: {planDone}/{planTotal} done ({uncheckedDone} ✓?)\n")
                : FormattableString.Invariant($"plan: {planDone}/{planTotal} done\n");
            PlanLine("plan:~summary", doneLine, shrink: false);
            var listed = new List<JsonObject>();
            var next = openSteps.Where(r => !Blocked(r)).ToArray();
            var current = next.FirstOrDefault();
            if (current is not null) { PlanDetailed(current, true); listed.Add(current); }
            foreach (var row in next.Where(r => r != current).Take(PlanNext)) { PlanTitle(row, "NEXT"); listed.Add(row); }
            var listedOpen = listed.Count;
            foreach (var row in blockedSteps.Where(r => !listed.Contains(r))) { PlanTitle(row, "BLOCKED"); listedOpen++; }
            // Completed steps render as a count (PT1); a completed step with no check is unverified and the
            // summary line marks it with ✓? so the harness and the seat can see it was completed without passing a check.
            var omittedOpen = next.Length - listed.Count;
            if (omittedOpen > 0)
                PlanLine("plan:~more", FormattableString.Invariant($"+{omittedOpen} more open steps (notebook list kind=plan-step)\n"), shrink: false);
        }

        var entries = new List<BriefingEntry>(planEntries.Count + 8);
        entries.AddRange(planEntries);
        var detailBudget = Math.Max(0, referenceBudget);
        void Detailed(JsonObject row)
        {
            var line = $"{row["id"]} r{row["revision"]} [{Kind(row)}/{row["status"]}] {row["text"]}\n";
            if (line.Length > detailBudget)
                line = $"{row["id"]} r{row["revision"]} [{Kind(row)}/{row["status"]}] (full text: notebook get)\n";
            else detailBudget -= line.Length;
            entries.Add(new BriefingEntry(row["id"]!.GetValue<string>(), line));
        }
        foreach (var row in obligations.Where(kept.Contains)) Detailed(row);
        foreach (var row in notes.Where(noteLines.ContainsKey)) entries.Add(new BriefingEntry(row["id"]!.GetValue<string>(), noteLines[row]));
        foreach (var row in sources.Where(kept.Contains)) Detailed(row);
        var omittedNotes = notes.Length - noteLines.Count;
        if (omittedNotes > 0)
            entries.Add(new BriefingEntry("~notes", FormattableString.Invariant($"+{omittedNotes} more notes/decisions; notebook op=search query=<words> finds them.\n")));
        var omitted = obligations.Length + sources.Length - kept.Count(r => !noteLines.ContainsKey(r));
        if (omitted > 0)
            entries.Add(new BriefingEntry("~omitted", FormattableString.Invariant($"+{omitted} older open records not listed (briefing budget); notebook list status=open shows them, complete the resolved ones.\n")));
        var available = Math.Max(0, maxCharacters - BriefingHeader.Length);
        if (entries.Sum(e => e.Line.Length) <= available) return entries;
        const string omittedLine = "+records omitted; notebook list/search retrieves full state.\n";
        var packed = new List<BriefingEntry>();
        available = Math.Max(0, available - omittedLine.Length);
        foreach (var entry in entries)
        {
            if (entry.Line.Length > available) continue;
            packed.Add(entry);
            available -= entry.Line.Length;
        }
        if (maxCharacters - BriefingHeader.Length >= omittedLine.Length)
            packed.Add(new BriefingEntry("~budget", omittedLine));
        return packed;
    }

    internal static string Lead(string text, int max)
    {
        var line = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return line.Length <= max ? line : line[..max] + "...";
    }
    /// <summary>Shrink one plan line to fit the remaining plan budget, keeping the prefix and cutting from the right.
    /// The last line ends with a newline, so cut the newline first.</summary>
    internal static string TrimPlanLine(string line, int budget)
    {
        if (line.Length <= budget) return line;
        if (budget >= 2 && line.EndsWith('\n')) line = line[..^1];
        if (line.Length <= budget) return line;
        if (line.Length > budget) line = line[..Math.Max(1, budget - 1)] + ".";
        return line;
    }
    public string StateReference() => string.Join(";", List().Where(r => r["status"]!.GetValue<string>() is "open" or "blocked")
        .Where(r => r["kind"]!.GetValue<string>() is "source" or "instruction" or "commitment")
        .Select(r => r["id"] + ":" + r["revision"]));
    public void Dispose() => _lease.Dispose();
}
