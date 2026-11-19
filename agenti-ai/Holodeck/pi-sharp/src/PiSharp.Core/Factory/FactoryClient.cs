// HTTP client for the AI Factory native surface: KB whoami plus the chat MCP tool calls the wait loop needs.
// Contract (chat_read, chat_wait, chat_post, chat_ack, chat_manage op=status; no kb_* tools):
//   1. whoami: GET {kbUrl}/whoami with Authorization: Bearer <token> -> 200 {slug,name,writableKinds}.
//      401 -> Unauthorized; anything else (non-2xx, timeout, connection error) -> Unreachable;
//      2xx whose body is not an object with a slug -> BadResponse.
//   2. inbox digest: POST {chatUrl}/mcp, stateless JSON-RPC (no initialize, no session id), body
//      {"jsonrpc":"2.0","id":<incrementing>,"method":"tools/call","params":{"name":<tool>,"arguments":{...}}}.
//      wait == 0 -> tool chat_read, arguments {"mark_read":<bool>} (never room/thread/since).
//      wait  > 0 -> tool chat_wait, arguments {"timeout_seconds":<1..90>,"scope":"inbox"} (never "rooms").
//      chat_wait never marks read, so when it returned inbox items and the caller asked for markRead we follow
//      with chat_read {"mark_read":true} and return that digest (ADR decision 1). If that follow-up read fails we
//      return the chat_wait digest rather than dropping items the caller has already been handed.
//      An inbox row carries either a nested "message" object or, since the chat server's heads-only digest
//      (wi-52), the same fields inline with a one-line "head" instead of the body. Both shapes are read; the
//      second sets FactoryMessage.HeadOnly, and the whole message is one chat_read away, named by
//      FactoryMessage.ReadPointer: thread=<thread id> for a reply, id=<message id> otherwise. Never
//      chat_read thread=<message id> on a row without a thread id of its own: that id is another message's
//      thread and the fetch lands in a different conversation (wrong-pointer incident, ops 99028).
//      The server latches items until chat_ack, so the same items come back on every digest and chat_wait
//      returns immediately while any unacknowledged item exists: callers must dedupe by the nested message id and
//      wait locally. "rooms" and "reachable" in the payload are ignored.
//   3. post: same envelope, tool chat_post, arguments {"body":...} plus only the non-null thread/to/kind.
//      Never passes room from the native path. PostAsync collapses the outcome to its bool; PostWithDetailAsync
//      keeps it as FactoryPostResult: Success, Refusal (4xx != 429 or a tool-level isError, with the HTTP status
//      and the response body - chat's exact refusal text) or Transient (5xx, 429, timeout, transport failure).
//   4. ack: same envelope, tool chat_ack, arguments {"message_ids":[...]} with the NESTED message.id of each inbox
//      item, never its inboxId (the two id sequences overlap). At most 100 ids per call, so larger sets go out in
//      chunks; non-positive ids are never sent (the server refuses the whole call) and come back as Unknown.
//      Payload {"actor":...,"acknowledged":[...],"alreadySeen":[...],"unknown":[...]}. The caller acks only
//      after its reply posted (or for items it deliberately ignores); the preflight egress probe never acks.
//   5. status (wi-84): same envelope, tool chat_manage, arguments {"op":"status"} plus "status" when non-null.
//      Empty string clears the line. True when the server accepted it. The wait loop sets this when a task
//      leaves the seat (idle plus what it last finished) and never posts availability into a room.
// MCP responses may be application/json (one JSON-RPC frame) or text/event-stream (the last data: event wins).
// The tool payload is JsonNode.Parse(result.content[0].text); result.isError == true, an "error" frame, or a
// non-2xx status is a failure.
// Every failure is "unavailable": WhoAmIAsync reports a FactoryFailure, ReadInboxAsync and AckAsync return null,
// PostAsync returns false and PostWithDetailAsync returns a non-Success FactoryPostResult (carrying the status and
// body when the server answered). Nothing here throws into the agent loop.
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using PiSharp.Core.Mcp;

namespace PiSharp.Core.Factory;

public enum FactoryFailure
{
    None,
    Unreachable,
    Unauthorized,
    BadResponse,
}

public sealed record FactoryIdentity(string Slug, string Name);

/// <summary><paramref name="Identity"/> is non-null exactly when <paramref name="Failure"/> is None.</summary>
public sealed record FactoryWhoAmI(FactoryIdentity? Identity, FactoryFailure Failure);

/// <summary>Harness-only snapshot of a KB item used to validate a retry-cap binding.</summary>
public sealed record FactoryKbEntity(string Kind, string? Status, IReadOnlyList<string> AssignedActors);

/// <summary>What a hand-in needs to know about a work item: whether it is an epic (attrs.type) that is not a placeholder, and
/// its name and summary, where its acceptance criteria are written.</summary>
public sealed record FactoryHandIn(bool IsEpic, string Objective, long? BudgetOutputTokens = null,
    IReadOnlyList<AcceptanceCriterion>? Criteria = null);

/// <summary>The rate band group on a seat's KB actor (adr-usage-meters, Amendment 1: rate-bands-a).
/// The band is a field written by the KB usage sweep, never by the seat; <see cref="Band"/> is exactly one of
/// ok/low/critical/paused, or null when the sweep has not classified yet or the KB did not answer. No
/// percentage or token counts ever come through this read.</summary>
public sealed record FactoryRateBand(string? Band, string? Since, string? ResetAt, bool Hybrid = false);

/// <summary><paramref name="HeadOnly"/> marks a message the digest described rather than carried: its Body is the
/// server's one-line head, not the message. The whole message is one fetch away, and the wrapper names it by Id
/// and <see cref="ReadPointer"/> - never by another message's thread, which sent seats to the wrong thread
/// (pi-sharp-wi-heads-only-wrapper-wrong-thread, wrong-pointer incident ops 99028).</summary>
public sealed record FactoryMessage(
    long Id,
    string Room,
    long? Thread,
    string Author,
    string Kind,
    string Body,
    IReadOnlyList<string> Mentions,
    string At,
    bool HeadOnly = false,
    IReadOnlyList<string>? About = null)
{
    /// <summary>The KB slugs the message carries (chat_post about=[...]); empty when it carried none. A heads-only
    /// row may not carry them until the whole message is fetched.</summary>
    public IReadOnlyList<string> AboutSlugs => About ?? [];

    /// <summary>The argument of the <c>chat_read</c> call that fetches this message whole: <c>thread=</c> with the
    /// thread id when the row carried one, <c>id=</c> with the message id when it did not. A thread-root message
    /// carries no thread id, so <c>thread=Id</c> and <c>id=Id</c> are the same call; a reply carries the ROOT id
    /// and naming the thread is enough to land in it. Either way the pointer identifies this row.</summary>
    public string ReadPointer => Thread is long t
        ? FormattableString.Invariant($"thread={t}")
        : FormattableString.Invariant($"id={Id}");
}

public sealed record FactoryInboxItem(long InboxId, long MessageId, string Reason, FactoryMessage Message);

/// <summary>One inbox item the parser refused, with whatever ids it carried (null when the payload had none) and
/// why it was dropped. Dropping is never silent: callers report these, but they never enter the run.</summary>
public sealed record FactoryDropped(long? InboxId, long? MessageId, string Reason);

public sealed record FactoryDigest(IReadOnlyList<FactoryInboxItem> Inbox, bool TimedOut, IReadOnlyList<FactoryDropped> Dropped)
{
    /// <summary>A digest that dropped nothing.</summary>
    public FactoryDigest(IReadOnlyList<FactoryInboxItem> inbox, bool timedOut) : this(inbox, timedOut, []) { }
}

/// <summary>The server's receipt for chat_ack, by nested message id: newly handled, already handled before, and
/// not in this actor's inbox at all (a wrong-namespace id, or a message that never reached it).</summary>
public sealed record FactoryAck(IReadOnlyList<long> Acknowledged, IReadOnlyList<long> AlreadySeen, IReadOnlyList<long> Unknown);

/// <summary>A KB note write's outcome: whether the server accepted it, and the underlying failure detail
/// (HTTP status, exception, or transport message) when it did not. Dropping to a bare boolean is what hid
/// the first live handover failure; the detail is the point of this type.</summary>
public sealed record KbWriteNoteResult(bool Ok, string? Error);

/// <summary>How a chat post ended (pi-sharp-refused-reply-retry-loop slice a): Success - the server took the
/// message; Refusal - chat said no to this very message (HTTP 4xx other than 429, or a tool-level isError), so
/// re-sending it unchanged would get the same answer; Transient - 5xx, 429, timeout or transport failure, which
/// is safe to retry later with backoff.</summary>
public enum FactoryPostOutcome
{
    Success,
    Refusal,
    Transient,
}

/// <summary>The full outcome of a chat post: the classification, the HTTP status when the server answered, the
/// body it answered with (chat's exact refusal text - what the old bare boolean dropped) and the failure detail.
/// <see cref="Ok"/> collapses it back to the boolean PostAsync always returned.</summary>
public sealed record FactoryPostResult(FactoryPostOutcome Outcome, int? StatusCode, string? Body, string? Error)
{
    /// <summary>True exactly when the server accepted the message - the old PostAsync boolean, unchanged.</summary>
    public bool Ok => Outcome == FactoryPostOutcome.Success;
}

public sealed class FactoryClient : IDisposable
{
    public const int MaxWaitSeconds = 90;

    /// <summary>The most message ids the server accepts in one chat_ack call.</summary>
    public const int MaxAckIds = 100;

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Dedicated deadline for KB note-write acknowledgement (15s by default), separate from the
    /// base tool-call timeout. Measured KB embedding latency reaches 2.6–8s; 15s provides headroom while
    /// remaining bounded. Caller cancellation can end it earlier.</summary>
    public static readonly TimeSpan DefaultNoteDeadline = TimeSpan.FromSeconds(15);

    private const string ProtocolVersion = StreamableHttpTransport.ProtocolVersion;

    private readonly HttpClient _http;
    private readonly Uri _whoAmI;
    private readonly Uri _chat;
    private readonly Uri _kb;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _noteDeadline;
    private long _nextId;

    public FactoryClient(string kbUrl, string chatUrl, string? token, HttpMessageHandler? handler = null, TimeSpan? timeout = null, TimeSpan? noteDeadline = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kbUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(chatUrl);
        _whoAmI = new Uri(kbUrl.TrimEnd('/') + "/whoami", UriKind.Absolute);
        _chat = new Uri(chatUrl.TrimEnd('/') + "/mcp", UriKind.Absolute);
        _kb = new Uri(kbUrl.TrimEnd('/') + "/mcp", UriKind.Absolute);
        _timeout = timeout ?? DefaultTimeout;
        _noteDeadline = noteDeadline ?? DefaultNoteDeadline;
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: true);
        // Per-request cancellation carries the deadline so a chat_wait long poll can exceed the base timeout.
        _http.Timeout = Timeout.InfiniteTimeSpan;
        if (!string.IsNullOrEmpty(token))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public Uri WhoAmIEndpoint => _whoAmI;

    public Uri ChatEndpoint => _chat;

    /// <summary>Asks the KB who this token belongs to. Never throws; failures come back as a
    /// <see cref="FactoryFailure"/> with a null identity.</summary>
    public async Task<FactoryWhoAmI> WhoAmIAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_timeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, _whoAmI);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return new FactoryWhoAmI(null, FactoryFailure.Unauthorized);
            if (!response.IsSuccessStatusCode)
                return new FactoryWhoAmI(null, FactoryFailure.Unreachable);
            var body = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            var identity = ParseIdentity(body);
            return identity is null
                ? new FactoryWhoAmI(null, FactoryFailure.BadResponse)
                : new FactoryWhoAmI(identity, FactoryFailure.None);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new FactoryWhoAmI(null, FactoryFailure.Unreachable);
        }
    }

    /// <summary>Reads the inbox digest. <paramref name="waitSeconds"/> 0 calls chat_read immediately; 1..90 long-polls
    /// with chat_wait and, when items arrived and <paramref name="markRead"/> is set, consumes them with a follow-up
    /// chat_read. Null when the server is unavailable or answered with an error. Never throws.</summary>
    public async Task<FactoryDigest?> ReadInboxAsync(int waitSeconds, bool markRead, CancellationToken cancellationToken = default)
    {
        waitSeconds = Math.Clamp(waitSeconds, 0, MaxWaitSeconds);
        if (waitSeconds == 0)
        {
            var read = await CallToolAsync("chat_read", ReadArguments(markRead), _timeout, cancellationToken).ConfigureAwait(false);
            return read is null ? null : ParseDigest(read);
        }

        var arguments = new JsonObject { ["timeout_seconds"] = waitSeconds, ["scope"] = "inbox" };
        var waited = await CallToolAsync("chat_wait", arguments, _timeout + TimeSpan.FromSeconds(waitSeconds), cancellationToken).ConfigureAwait(false);
        if (waited is null) return null;
        var digest = ParseDigest(waited);
        if (digest is null || !markRead || digest.Inbox.Count == 0) return digest;

        // chat_wait never marks read: consume the same items with chat_read and hand back that digest, carrying
        // both digests' drops so nothing the parser refused goes unreported.
        var consumed = await CallToolAsync("chat_read", ReadArguments(markRead: true), _timeout, cancellationToken).ConfigureAwait(false);
        if ((consumed is null ? null : ParseDigest(consumed)) is not { } marked) return digest;
        return marked with { Dropped = Concat(digest.Dropped, marked.Dropped) };
    }

    /// <summary>Fetch exactly the addressed message, without acknowledging it or expanding an unrelated thread.</summary>
    public async Task<FactoryMessage?> ReadFullMessageAsync(FactoryMessage message, CancellationToken ct)
    {
        if (!message.HeadOnly) return message;
        var text = await CallToolAsync("chat_read", new JsonObject { ["room"] = message.Room,
            ["since"] = message.Id - 1, ["limit"] = 1, ["mark_read"] = false }, _timeout, ct).ConfigureAwait(false);
        try
        {
            var rows = JsonNode.Parse(text ?? "null")?["messages"]?.AsArray();
            var row = rows?.FirstOrDefault(r => r?["id"]?.GetValue<long>() == message.Id);
            if (row?["body"]?.GetValue<string>() is not { } body) return null;
            var about = ReadStrings(row["about"]);
            return message with { Body = body, HeadOnly = false, About = about.Count > 0 ? about : message.About };
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException) { return null; }
    }

    /// <summary>The KB entities <paramref name="slug"/> belongs to: the slugs its outgoing <c>belongs_to</c> and
    /// <c>part_of</c> links name (kb_get on the KB MCP, same token). Empty when it has none; null when the KB did not
    /// answer or knows no such entity. Never throws. Places a work item in its product's backlog.</summary>
    public async Task<IReadOnlyList<string>?> KbParentsAsync(string slug, CancellationToken cancellationToken = default)
    {
        var text = await CallToolAsync(_kb, "kb_get", new JsonObject { ["slug"] = slug, ["format"] = "concise" }, _timeout, cancellationToken).ConfigureAwait(false);
        if (text is null) return null;
        try
        {
            if (FindLinks(JsonNode.Parse(text), 3) is not { } links) return null;
            var parents = new List<string>();
            foreach (var link in links.OfType<JsonObject>())
                if (ReadString(link["rel"]) is "belongs_to" or "part_of" && ReadString(link["slug"]) is { Length: > 0 } parent)
                    parents.Add(parent);
            return parents;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>The actor that holds <paramref name="role"/> now (the role's incoming <c>has_role</c> links, kb_get on the KB
    /// MCP, same token), or null when the KB did not answer or nobody holds it. Never throws. A handover goes to whoever
    /// leads the team today, never to a seat named in code (audit 2026-10-24: team-lead-2 was hard-coded).</summary>
    public async Task<string?> KbRoleHolderAsync(string role, CancellationToken cancellationToken = default)
    {
        var text = await CallToolAsync(_kb, "kb_get", new JsonObject { ["slug"] = role, ["format"] = "concise" }, _timeout, cancellationToken).ConfigureAwait(false);
        if (text is null) return null;
        try
        {
            if (FindLinks(JsonNode.Parse(text), 3) is not { } links) return null;
            foreach (var link in links.OfType<JsonObject>())
                if (ReadString(link["rel"]) == "<-has_role" && ReadString(link["kind"]) is null or "actor" && ReadString(link["slug"]) is { Length: > 0 } holder)
                    return holder;
            return null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
    }

    /// <summary>The KB's "did you mean" for a URL, git remote or host a command failed on (kb_resolve on the KB MCP,
    /// same token). Only a <c>misspelled</c> or <c>known_host</c> answer comes back, as the KB's hint; a known or
    /// unknown name, or a KB that did not answer, is null. Never throws.</summary>
    public async Task<string?> KbResolveAsync(string name, CancellationToken cancellationToken = default)
    {
        var text = await CallToolAsync(_kb, "kb_resolve", new JsonObject { ["name"] = name }, _timeout, cancellationToken).ConfigureAwait(false);
        if (text is null) return null;
        try
        {
            var root = JsonNode.Parse(text);
            return ReadString(root?["verdict"]) is "misspelled" or "known_host" ? ReadString(root?["hint"]) : null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>Reads one KB entity through the attached seat's KB token. Null means unavailable or malformed.</summary>
    public async Task<FactoryKbEntity?> KbEntityAsync(string slug, CancellationToken cancellationToken = default)
    {
        var text = await CallToolAsync(_kb, "kb_get", new JsonObject { ["slug"] = slug, ["format"] = "concise" }, _timeout, cancellationToken).ConfigureAwait(false);
        if (text is null) return null;
        try
        {
            var root = JsonNode.Parse(text);
            if (root is not JsonObject entity || ReadString(entity["kind"]) is not { } kind) return null;
            var assigned = new List<string>();
            if (FindLinks(root, 3) is { } links)
                foreach (var link in links.OfType<JsonObject>())
                    if (ReadString(link["rel"]) == "assigned_to" && ReadString(link["slug"]) is { Length: > 0 } actor)
                        assigned.Add(actor);
            return new FactoryKbEntity(kind, ReadString(entity["status"]), assigned);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
    }

    /// <summary>The rate band the KB usage sweep has written on <paramref name="actor"/> (kb_get on the KB MCP,
    /// same token): <c>attrs.rate_band</c> (ok/low/critical/paused), <c>rate_band_since</c> and <c>rate_reset_at</c>
    /// (adr-usage-meters, Amendment 1). The read is detailed-format because format=concise omits attrs.
    /// <see cref="FactoryRateBand.Band"/> is null when the sweep has not classified yet; the whole result is null
    /// when the KB did not answer. Never throws. A band value outside the four is treated as unclassified: the
    /// writer is the KB sweep and anything else is a defect against the amendment, not a band to act on.</summary>
    public async Task<FactoryRateBand?> KbActorRateBandAsync(string actor, CancellationToken cancellationToken = default)
    {
        var text = await CallToolAsync(_kb, "kb_get", new JsonObject { ["slug"] = actor }, _timeout, cancellationToken).ConfigureAwait(false);
        if (text is null) return null;
        try
        {
            if (JsonNode.Parse(text) is not JsonObject root) return null;
            var band = ReadString(root["attrs"]?["rate_band"]);
            band = band is "ok" or "low" or "critical" or "paused" ? band : null;
            return new FactoryRateBand(band, ReadString(root["attrs"]?["rate_band_since"]), ReadString(root["attrs"]?["rate_reset_at"]))
            {
                Hybrid = root["attrs"]?["rate_hybrid"] is JsonValue h && h.TryGetValue<bool>(out var hybrid) && hybrid,
            };
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
    }

    /// <summary>The work items <paramref name="actor"/> holds in <c>ready</c> or <c>in_progress</c> (kb_list on the KB
    /// MCP, same token), as (slug, status), in_progress first. Empty when it holds none; null when the KB did not
    /// answer. Never throws. This is what makes a seat busy (rule work-item-assignment v14); blocked items do not.</summary>
    /// <summary>The current text of document <paramref name="key"/> on entity <paramref name="slug"/> (kb_get doc=key), or null when
    /// the entity has none, it is retired, or the KB did not answer. Never throws.</summary>
    public async Task<string?> KbDocBodyAsync(string slug, string key, CancellationToken cancellationToken = default)
    {
        var text = await CallToolAsync(_kb, "kb_get", new JsonObject { ["slug"] = slug, ["doc"] = key }, _timeout, cancellationToken).ConfigureAwait(false);
        if (text is null) return null;
        try
        {
            if (JsonNode.Parse(text) is not JsonObject root || root["docs"] is not JsonArray docs) return null;
            foreach (var doc in docs.OfType<JsonObject>())
                if (ReadString(doc["key"]) == key && ReadString(doc["retiredAt"]) is null && ReadString(doc["body"]) is { Length: > 0 } body)
                    return body;
            return null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
    }

    public async Task<IReadOnlyList<(string Slug, string Status)>?> KbHeldWorkItemsAsync(string actor, CancellationToken cancellationToken = default, bool occupying = false) =>
        await KbHeldWorkAsync(actor, cancellationToken, occupying).ConfigureAwait(false) is { } held ? [.. held.Select(h => (h.Slug, h.Status))] : null;

    /// <summary>One work item a seat holds, and the test run it waits on (kb_transition waiting_on), if any.</summary>
    public sealed record HeldItem(string Slug, string Status, string? WaitingOn);

    /// <summary><see cref="KbHeldWorkItemsAsync"/> with the test run each item waits on.</summary>
    public async Task<IReadOnlyList<HeldItem>?> KbHeldWorkAsync(string actor, CancellationToken cancellationToken = default, bool occupying = false)
    {
        var args = new JsonObject
        {
            ["kind"] = "work_item", ["assigned_to"] = actor, ["status"] = "ready,in_progress", ["limit"] = 20,
        };
        // kb 1.0.65: occupying=true leaves out an in_progress item waiting on a clock (kb_transition wait_until), so a
        // seat that only waits for a reading is free for other work. Sent only when asked: the quota handover wants all.
        if (occupying) args["occupying"] = true;
        var text = await CallToolAsync(_kb, "kb_list", args, _timeout, cancellationToken).ConfigureAwait(false);
        if (text is null) return null;
        try
        {
            if (JsonNode.Parse(text) is not JsonObject root || root["items"] is not JsonArray items) return null;
            var held = new List<HeldItem>();
            foreach (var item in items.OfType<JsonObject>())
                if (ReadString(item["slug"]) is { Length: > 0 } slug)
                    held.Add(new(slug, ReadString(item["status"]) ?? "", ReadString(item["waitingOn"]) is { Length: > 0 } run ? run : null));
            return [.. held.OrderBy(h => h.Status == "in_progress" ? 0 : 1)];
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
    }

    /// <summary>The work items the seat can work on now: ready or in progress (kb_list occupying=true, which leaves out a wait
    /// on a clock), less the ones waiting on a test run (<c>waitingOn</c>), whose end wakes the seat by itself. In progress
    /// first. Null when the KB did not answer.</summary>
    public async Task<IReadOnlyList<(string Slug, string Status)>?> KbWorkableItemsAsync(string actor, CancellationToken cancellationToken = default)
    {
        var args = new JsonObject
        {
            ["kind"] = "work_item", ["assigned_to"] = actor, ["status"] = "ready,in_progress", ["occupying"] = true, ["limit"] = 20,
        };
        var text = await CallToolAsync(_kb, "kb_list", args, _timeout, cancellationToken).ConfigureAwait(false);
        if (text is null) return null;
        try
        {
            if (JsonNode.Parse(text) is not JsonObject root || root["items"] is not JsonArray items) return null;
            var workable = new List<(string Slug, string Status)>();
            foreach (var item in items.OfType<JsonObject>())
                if (ReadString(item["slug"]) is { Length: > 0 } slug && ReadString(item["waitingOn"]) is not { Length: > 0 }
                    && ReadString(item["waitingUntil"]) is not { Length: > 0 })
                    workable.Add((slug, ReadString(item["status"]) ?? ""));
            return [.. workable.OrderBy(h => h.Status == "in_progress" ? 0 : 1)];
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
    }

    /// <summary>
    /// The seat's standing text, read from the KB at start: pi-sharp's <c>seat-standing</c> (every seat), then for each role
    /// the actor holds its <c>job-description</c> and its <c>seat-standing</c>. digital-scribe, 2026-10-13: shared texts live
    /// in the KB, where one edit reaches every seat, never in .md files on the hosts or in repositories (until then the
    /// launcher assembled this from /etc/pi-sharp/roles/*.md). Null when the actor cannot be read; with
    /// <paramref name="whole"/>, also when any document could not be read (a seat that has its texts keeps them rather than
    /// take a part of them; a document the role does not have is not a failure).
    /// </summary>
    public async Task<string?> KbSeatStandingTextAsync(string actor, CancellationToken cancellationToken = default, bool whole = false)
    {
        var failed = false;
        async Task<string?> Doc(string slug, string key)
        {
            var call = await CallToolDetailedAsync(_kb, "kb_get", new JsonObject { ["slug"] = slug, ["doc"] = key }, _timeout, cancellationToken).ConfigureAwait(false);
            var text = call.Text;
            // The KB's own refusal (a document the role does not have) has its words in Body; no answer at all has none.
            if (text is null && call.Body is null) failed = true;
            if (text is null) return null;
            try
            {
                return (JsonNode.Parse(text)?["docs"] as JsonArray)?.OfType<JsonObject>()
                    .Where(d => d["key"]?.GetValue<string>() == key)
                    .Select(d => d["body"]?.GetValue<string>()).FirstOrDefault(b => !string.IsNullOrWhiteSpace(b));
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException) { return null; }
        }

        var entity = await CallToolAsync(_kb, "kb_get", new JsonObject { ["slug"] = actor }, _timeout, cancellationToken).ConfigureAwait(false);
        if (entity is null) return null;
        List<string> roles;
        try
        {
            roles = ((JsonNode.Parse(entity)?["links"] as JsonArray) ?? []).OfType<JsonObject>()
                .Where(l => l["rel"]?.GetValue<string>() == "has_role")
                .Select(l => l["slug"]?.GetValue<string>()).OfType<string>().ToList();
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException) { return null; }
        var parts = new List<string>();
        if (await Doc("pi-sharp", "seat-standing").ConfigureAwait(false) is { } common) parts.Add(common);
        foreach (var role in roles)
        {
            if (await Doc(role, "job-description").ConfigureAwait(false) is { } jd)
                parts.Add($"# Your role in the KB: `{role}` (you are actor {actor})\n\n{jd}");
            if (await Doc(role, "seat-standing").ConfigureAwait(false) is { } standing) parts.Add(standing);
        }
        return parts.Count == 0 || whole && failed ? null : string.Join("\n\n", parts);
    }

    /// <summary>A work item's name and summary (an epic's outcome, acceptance criteria and proof), cut to
    /// <paramref name="maxCharacters"/>; null when the KB did not answer.</summary>
    public async Task<string?> KbItemObjectiveAsync(string slug, int maxCharacters = 3000, CancellationToken cancellationToken = default)
    {
        var text = await CallToolAsync(_kb, "kb_get", new JsonObject { ["slug"] = slug, ["format"] = "concise" }, _timeout, cancellationToken).ConfigureAwait(false);
        if (text is null) return null;
        try
        {
            if (JsonNode.Parse(text) is not JsonObject item) return null;
            var objective = $"{ReadString(item["name"]) ?? slug}\n{ReadString(item["summary"]) ?? ""}".Trim();
            return objective.Length <= maxCharacters ? objective : objective[..maxCharacters] + $"\n[... the rest: kb_get {slug}]";
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException) { return null; }
    }

    /// <summary>Whether <paramref name="slug"/> is an epic to hand in criterion by criterion, with its objective; null when the KB
    /// did not answer. The full read: the concise one carries no attrs.</summary>
    public async Task<FactoryHandIn?> KbHandInAsync(string slug, CancellationToken cancellationToken = default)
    {
        var text = await CallToolAsync(_kb, "kb_get", new JsonObject { ["slug"] = slug }, _timeout, cancellationToken).ConfigureAwait(false);
        if (text is null) return null;
        try
        {
            if (JsonNode.Parse(text) is not JsonObject item || ReadString(item["kind"]) != "work_item") return null;
            var attrs = item["attrs"] as JsonObject;
            var placeholder = attrs?["placeholder"] is JsonValue p && (p.TryGetValue<bool>(out var b) ? b : p.TryGetValue<string>(out var t) && t.Length > 0);
            var objective = $"{ReadString(item["name"]) ?? slug}\n{ReadString(item["summary"]) ?? ""}".Trim();
            var criteria = AcceptanceCriteria.Read(attrs?["acceptance_criteria"], objective);
            if (objective.Length > 3000) objective = objective[..3000] + $"\n[... the rest: kb_get {slug}]";
            long? budget = attrs?["budget_output_tokens"] is JsonValue bv && long.TryParse(bv.ToJsonString().Trim('"'), out var tokens) && tokens > 0 ? tokens : null;
            return new FactoryHandIn(ReadString(attrs?["type"]) == "epic" && !placeholder, objective, budget, criteria);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or InvalidDataException) { return null; }
    }

    /// <summary>The factory's switches the seats follow (kb 1.0.306 <c>GET /seat/switches</c>, the owner flips them on the Engine Room
    /// page): name to on/off. Null when the hub did not answer or is older.</summary>
    public async Task<IReadOnlyDictionary<string, bool>?> KbSwitchesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            using var response = await _http.GetAsync(new Uri(_kb, "/seat/switches"), deadline.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            if (JsonNode.Parse(await response.Content.ReadAsStringAsync(deadline.Token).ConfigureAwait(false)) is not JsonObject switches) return null;
            var result = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var (name, value) in switches)
                if (value is JsonValue v && v.TryGetValue<bool>(out var on)) result[name] = on;
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or InvalidOperationException
            || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested) { return null; }
    }

    /// <summary>The roles an actor holds (its has_role links); null when the KB did not answer.</summary>
    public async Task<IReadOnlyCollection<string>?> KbActorRolesAsync(string actor, CancellationToken cancellationToken = default)
    {
        var entity = await CallToolAsync(_kb, "kb_get", new JsonObject { ["slug"] = actor, ["format"] = "concise" }, _timeout, cancellationToken).ConfigureAwait(false);
        if (entity is null) return null;
        try
        {
            return [.. ((JsonNode.Parse(entity)?["links"] as JsonArray) ?? []).OfType<JsonObject>()
                .Where(l => l["rel"]?.GetValue<string>() == "has_role")
                .Select(l => l["slug"]?.GetValue<string>()).OfType<string>()];
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException) { return null; }
    }

    /// <summary>Loads active KB rule documents, bounded to 100 entities/documents. Null means the KB could
    /// not be queried; callers should keep locally configured hooks and continue fail-open.</summary>
    public async Task<IReadOnlyList<string>?> KbActiveRuleDocumentsAsync(CancellationToken cancellationToken = default)
    {
        var listed = await CallToolAsync(_kb, "kb_list", new JsonObject
        {
            ["kind"] = "rule", ["status"] = "active", ["limit"] = 100,
        }, _timeout, cancellationToken).ConfigureAwait(false);
        if (listed is null) return null;
        try
        {
            var root = JsonNode.Parse(listed) as JsonObject;
            if (root?["items"] is not JsonArray items) return null;
            var documents = new List<string>();
            foreach (var item in items.OfType<JsonObject>().Take(100))
            {
                var slug = item["slug"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(slug)) continue;
                var entityText = await CallToolAsync(_kb, "kb_get", new JsonObject { ["slug"] = slug }, _timeout, cancellationToken).ConfigureAwait(false);
                if (entityText is null || JsonNode.Parse(entityText) is not JsonObject entity || entity["docs"] is not JsonArray docs) continue;
                foreach (var doc in docs.OfType<JsonObject>().Take(20))
                {
                    var key = doc["key"]?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(key)) continue;
                    var docText = await CallToolAsync(_kb, "kb_get", new JsonObject { ["slug"] = slug, ["doc"] = key }, _timeout, cancellationToken).ConfigureAwait(false);
                    if (docText is null) continue;
                    var body = FindDocumentBody(JsonNode.Parse(docText));
                    if (body is not null) documents.Add(body);
                    if (documents.Count >= 100) return documents;
                }
            }
            return documents;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
    }

    private static string? FindDocumentBody(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            if (obj["body"] is JsonValue value && value.TryGetValue<string>(out var body)) return body;
            foreach (var child in obj.Select(p => p.Value))
                if (FindDocumentBody(child) is { } found) return found;
        }
        else if (node is JsonArray array)
            foreach (var child in array)
                if (FindDocumentBody(child) is { } found) return found;
        return null;
    }

    /// <summary>Harness-owned KB status transition; deliberately separate from model-exposed tools.</summary>
    public async Task<bool> KbTransitionAsync(string slug, string status, string note, CancellationToken cancellationToken = default,
        string? assignTo = null)
    {
        var arguments = new JsonObject { ["work_item"] = slug, ["to_status"] = status, ["note"] = note };
        if (assignTo is not null) arguments["assign_to"] = assignTo;
        var result = await CallToolAsync(_kb, "kb_transition", arguments, _timeout, cancellationToken).ConfigureAwait(false);
        return result is not null;
    }

    /// <summary>Stores a timestamped note on a KB entity; null key intentionally creates a new note version.
    /// True when the server accepted it; never throws.</summary>
    public async Task<bool> KbWriteNoteAsync(string slug, string title, string body, CancellationToken cancellationToken = default)
    {
        var result = await KbWriteNoteWithDetailAsync(slug, title, body, cancellationToken).ConfigureAwait(false);
        return result.Ok;
    }

    /// <summary>As <see cref="KbWriteNoteAsync(string, string, string, CancellationToken)"/>, surfacing the underlying failure
    /// detail (HTTP status, exception, or transport message) in the result. Never throws.</summary>
    public async Task<KbWriteNoteResult> KbWriteNoteWithDetailAsync(string slug, string title, string body, CancellationToken cancellationToken = default)
    {
        var call = await CallToolDetailedAsync(_kb, "kb_write", new JsonObject
        {
            ["op"] = "doc", ["doc"] = new JsonObject { ["entity"] = slug, ["title"] = title, ["body"] = body },
        }, _noteDeadline, cancellationToken).ConfigureAwait(false);
        return new KbWriteNoteResult(call.Text is not null, call.Error);
    }

    /// <summary>One document's body on an entity; "" when the entity has no such document, null when the KB did not answer.</summary>
    public async Task<string?> KbDocAsync(string slug, string key, CancellationToken cancellationToken = default)
    {
        var call = await CallToolDetailedAsync(_kb, "kb_get", new JsonObject { ["slug"] = slug, ["doc"] = key }, _timeout, cancellationToken).ConfigureAwait(false);
        // The KB's own refusal (no such document) has its words in Body; no answer at all has none.
        if (call.Text is null) return call.Body is null ? null : "";
        try
        {
            return (JsonNode.Parse(call.Text)?["docs"] as JsonArray)?.OfType<JsonObject>()
                .Where(d => d["key"]?.GetValue<string>() == key).Select(d => d["body"]?.GetValue<string>()).FirstOrDefault() ?? "";
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException) { return null; }
    }

    /// <summary>Writes one keyed document on an entity (a new version of it); true when the KB took it. Never throws.</summary>
    public async Task<bool> KbWriteDocAsync(string slug, string key, string title, string body, CancellationToken cancellationToken = default)
    {
        var call = await CallToolDetailedAsync(_kb, "kb_write", new JsonObject
        {
            ["op"] = "doc", ["doc"] = new JsonObject { ["entity"] = slug, ["key"] = key, ["title"] = title, ["body"] = body },
        }, _noteDeadline, cancellationToken).ConfigureAwait(false);
        return call.Text is not null;
    }

    /// <summary>The first "links" array in a kb_get payload, at the top or nested a few objects down.</summary>
    private static JsonArray? FindLinks(JsonNode? node, int depth)
    {
        if (node is not JsonObject obj || depth < 0) return null;
        if (obj["links"] is JsonArray links) return links;
        foreach (var (_, child) in obj)
            if (FindLinks(child, depth - 1) is { } found) return found;
        return null;
    }

    /// <summary>Posts one chat message. Exactly the non-null arguments are sent; room is never sent. True when the
    /// server accepted it. Never throws. <see cref="PostWithDetailAsync"/> is the same call that keeps why.</summary>
    public async Task<bool> PostAsync(string body, long? thread = null, string? to = null, string? kind = null, CancellationToken cancellationToken = default) =>
        (await PostWithDetailAsync(body, thread, to, kind, cancellationToken).ConfigureAwait(false)).Ok;

    /// <summary>As <see cref="PostAsync(string, long?, string?, string?, CancellationToken)"/>, reporting the outcome
    /// instead of collapsing it: Success; Refusal (4xx other than 429, or a tool-level isError) with the HTTP status
    /// and the server's body text - chat's exact words, which is what a seat log needs to say why the message was
    /// not taken; or Transient (5xx, 429, timeout, transport failure), safe to retry later with backoff. The bool
    /// callers see no difference. Never throws.</summary>
    public async Task<FactoryPostResult> PostWithDetailAsync(string body, long? thread = null, string? to = null, string? kind = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(body);
        var arguments = new JsonObject { ["body"] = body };
        if (thread is not null) arguments["thread"] = thread.Value;
        if (to is not null) arguments["to"] = to;
        if (kind is not null) arguments["kind"] = kind;
        var call = await CallToolDetailedAsync(_chat, "chat_post", arguments, _timeout, cancellationToken).ConfigureAwait(false);
        return ClassifyPost(call);
    }

    /// <summary>A chat_post ToolCall as its outcome: a text payload is Success; an HTTP status decides between
    /// Refusal (4xx != 429) and Transient (5xx, 429); no status at all is a timeout or transport failure, hence
    /// Transient; a 2xx that carried no text is a Refusal only when the server stated why (its words are the
    /// Body), otherwise Transient.</summary>
    private static FactoryPostResult ClassifyPost(ToolCall call)
    {
        if (call.Text is not null)
            return new FactoryPostResult(FactoryPostOutcome.Success, call.StatusCode, null, null);
        var outcome = call.StatusCode switch
        {
            null => FactoryPostOutcome.Transient,
            >= 400 and < 500 and not 429 => FactoryPostOutcome.Refusal,
            429 => FactoryPostOutcome.Transient,
            >= 500 => FactoryPostOutcome.Transient,
            _ => call.Body is not null ? FactoryPostOutcome.Refusal : FactoryPostOutcome.Transient,
        };
        return new FactoryPostResult(outcome, call.StatusCode, call.Body, call.Error);
    }

    /// <summary>Harness-owned channel post with an explicit room; kept separate from reply PostAsync.</summary>
    public async Task<bool> PostRoomAsync(string room, string body, IReadOnlyList<string>? about = null, CancellationToken cancellationToken = default)
    {
        var arguments = new JsonObject { ["room"] = room, ["body"] = body };
        if (about is { Count: > 0 })
        {
            arguments["about"] = new JsonArray(about.Select(slug => (JsonNode)JsonValue.Create(slug)!).ToArray());
        }
        return await CallToolAsync("chat_post", arguments, _timeout, cancellationToken).ConfigureAwait(false) is not null;
    }

    /// <summary>Marks inbox items handled so the latch stops returning them. <paramref name="messageIds"/> are nested
    /// message ids (never inbox ids); duplicates are sent once, at most <see cref="MaxAckIds"/> per call, and
    /// non-positive ids are reported as Unknown without being sent. Empty input returns an empty receipt without a
    /// call. Null when any call failed or answered something that is not a receipt. Never throws.</summary>
    public async Task<FactoryAck?> AckAsync(IReadOnlyCollection<long> messageIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messageIds);
        var acknowledged = new List<long>();
        var alreadySeen = new List<long>();
        var unknown = new List<long>();
        var send = new List<long>();
        foreach (var id in messageIds.Distinct())
            (id > 0 ? send : unknown).Add(id);

        foreach (var chunk in send.Chunk(MaxAckIds))
        {
            var ids = new JsonArray();
            foreach (var id in chunk) ids.Add((JsonNode)JsonValue.Create(id));
            var text = await CallToolAsync("chat_ack", new JsonObject { ["message_ids"] = ids }, _timeout, cancellationToken).ConfigureAwait(false);
            if ((text is null ? null : ParseAck(text)) is not { } receipt) return null;
            acknowledged.AddRange(receipt.Acknowledged);
            alreadySeen.AddRange(receipt.AlreadySeen);
            unknown.AddRange(receipt.Unknown);
        }
        return new FactoryAck(acknowledged, alreadySeen, unknown);
    }

    /// <summary>Sets this actor's one-line chat status (<c>chat_manage op=status</c>). Pass a non-empty
    /// <paramref name="status"/> to set it, empty or null to clear. True when the server accepted it.
    /// Never throws. Does not post a message.</summary>
    public async Task<bool> SetStatusAsync(string? status, CancellationToken cancellationToken = default)
    {
        var arguments = new JsonObject { ["op"] = "status" };
        if (status is not null) arguments["status"] = status;
        return await CallToolAsync("chat_manage", arguments, _timeout, cancellationToken).ConfigureAwait(false) is not null;
    }

    /// <summary>Parses a /whoami body. Null when it is not an object with a slug.</summary>
    public static FactoryIdentity? ParseIdentity(string body)
    {
        try
        {
            if (JsonNode.Parse(body) is not JsonObject obj) return null;
            var slug = ReadString(obj["slug"]);
            if (string.IsNullOrEmpty(slug)) return null;
            return new FactoryIdentity(slug, ReadString(obj["name"]) ?? slug);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>Parses a chat_read / chat_wait payload. TimedOut comes from "timedOut" (false when absent).
    /// Items whose top-level messageId disagrees with message.id are dropped as ambiguous; every skipped item is
    /// recorded in <see cref="FactoryDigest.Dropped"/> with the ids it carried and a stated reason, so no drop is
    /// silent. Null when the text is not a JSON object.</summary>
    public static FactoryDigest? ParseDigest(string toolText)
    {
        JsonNode? node;
        try { node = JsonNode.Parse(toolText); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
        if (node is not JsonObject obj) return null;

        var timedOut = obj["timedOut"] is JsonValue flag && flag.TryGetValue<bool>(out var t) && t;
        var inbox = new List<FactoryInboxItem>();
        var dropped = new List<FactoryDropped>();
        if (obj["inbox"] is JsonArray arr)
        {
            foreach (var entry in arr)
            {
                if (entry is not JsonObject item)
                {
                    dropped.Add(new FactoryDropped(null, null, "not an object"));
                    continue;
                }
                var messageId = ReadLong(item["messageId"]);
                if (item["message"] is not JsonObject m)
                {
                    // Heads-only digest (chat server wi-52, live since 2026-09-19): the row describes the message
                    // inline - room, author, kind, one-line head - and carries no nested object. Refusing those
                    // rows made every seat deaf to its own inbox, so take the head as the body and say so; the
                    // agent reads the rest with chat_read thread=<id>.
                    if (ParseHead(item, messageId) is { } head)
                    {
                        inbox.Add(head);
                        continue;
                    }
                    dropped.Add(new FactoryDropped(ReadLong(item["inboxId"]), messageId, "no message object"));
                    continue;
                }
                var nestedId = ReadLong(m["id"]);
                // Both ids present and disagreeing: the item is ambiguous. Drop it rather than guess which one
                // callers should dedupe and reply by - but say so, never silently.
                if (messageId is not null && nestedId is not null && messageId != nestedId)
                {
                    dropped.Add(new FactoryDropped(
                        ReadLong(item["inboxId"]),
                        messageId,
                        FormattableString.Invariant($"messageId {messageId.Value} disagrees with message.id {nestedId.Value}")));
                    continue;
                }
                var id = nestedId ?? messageId;
                if (id is null)
                {
                    dropped.Add(new FactoryDropped(ReadLong(item["inboxId"]), null, "no message id"));
                    continue;
                }
                var message = new FactoryMessage(
                    id.Value,
                    ReadString(m["room"]) ?? "",
                    ReadLong(m["thread"]),
                    ReadString(m["author"]) ?? "",
                    ReadString(m["kind"]) ?? "note",
                    ReadString(m["body"]) ?? "",
                    ReadStrings(m["mentions"]),
                    m["at"]?.ToString() ?? "",
                    About: ReadStrings(m["about"]));
                inbox.Add(new FactoryInboxItem(
                    ReadLong(item["inboxId"]) ?? 0,
                    messageId ?? id.Value,
                    ReadString(item["reason"]) ?? "",
                    message));
            }
        }
        return new FactoryDigest(inbox, timedOut, dropped);
    }

    /// <summary>One heads-only inbox row (no nested message object) as an item whose body is the row's head.
    /// Null when the row carries no usable message id or no head at all - that is not a message, and the caller
    /// drops it with a stated reason as before.</summary>
    private static FactoryInboxItem? ParseHead(JsonObject item, long? messageId)
    {
        if (messageId is not > 0) return null;
        if (ReadString(item["head"]) is not { } head) return null;
        var message = new FactoryMessage(
            messageId.Value,
            ReadString(item["room"]) ?? "",
            ReadLong(item["thread"]),
            ReadString(item["author"]) ?? "",
            ReadString(item["kind"]) ?? "note",
            head,
            ReadStrings(item["mentions"]),
            item["at"]?.ToString() ?? "",
            HeadOnly: true,
            About: ReadStrings(item["about"]));
        return new FactoryInboxItem(
            ReadLong(item["inboxId"]) ?? 0,
            messageId.Value,
            ReadString(item["reason"]) ?? "",
            message);
    }

    /// <summary>Parses a chat_ack payload. Null unless it is an object carrying all three id arrays
    /// (acknowledged, alreadySeen, unknown); entries that are not ids are skipped.</summary>
    public static FactoryAck? ParseAck(string toolText)
    {
        try
        {
            if (JsonNode.Parse(toolText) is not JsonObject obj) return null;
            if (obj["acknowledged"] is not JsonArray acknowledged
                || obj["alreadySeen"] is not JsonArray alreadySeen
                || obj["unknown"] is not JsonArray unknown)
                return null;
            return new FactoryAck(ReadLongs(acknowledged), ReadLongs(alreadySeen), ReadLongs(unknown));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    private static JsonObject ReadArguments(bool markRead) => new() { ["mark_read"] = markRead };

    /// <summary>Both drop lists of a chat_wait plus its follow-up chat_read: neither may be lost.</summary>
    private static IReadOnlyList<FactoryDropped> Concat(IReadOnlyList<FactoryDropped> first, IReadOnlyList<FactoryDropped> second)
    {
        if (first.Count == 0) return second;
        if (second.Count == 0) return first;
        var all = new List<FactoryDropped>(first.Count + second.Count);
        all.AddRange(first);
        all.AddRange(second);
        return all;
    }

    /// <summary>One stateless tools/call POST. Returns the tool's text payload, or null for any failure
    /// (non-2xx, transport error, deadline, error frame, isError, missing text).</summary>
    private Task<string?> CallToolAsync(string tool, JsonObject arguments, TimeSpan deadline, CancellationToken cancellationToken) =>
        CallToolAsync(_chat, tool, arguments, deadline, cancellationToken);

    private async Task<string?> CallToolAsync(Uri endpoint, string tool, JsonObject arguments, TimeSpan deadline, CancellationToken cancellationToken)
    {
        var call = await CallToolDetailedAsync(endpoint, tool, arguments, deadline, cancellationToken).ConfigureAwait(false);
        return call.Text;
    }

    /// <summary>A tool call's outcome: its text payload, or the failure detail (HTTP status, exception, or
    /// transport message) when there is no text. StatusCode is the HTTP status when the server answered at all;
    /// Body is the response body of a non-2xx answer and the server's own words on a tool-level failure - what a
    /// seat log needs to say why a post was refused (pi-sharp-refused-reply-retry-loop slice a).</summary>
    private readonly record struct ToolCall(string? Text, string? Error, int? StatusCode = null, string? Body = null);

    private async Task<ToolCall> CallToolDetailedAsync(Uri endpoint, string tool, JsonObject arguments, TimeSpan deadline, CancellationToken cancellationToken)
    {
        int? status = null;
        try
        {
            var envelope = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = Interlocked.Increment(ref _nextId),
                ["method"] = "tools/call",
                ["params"] = new JsonObject { ["name"] = tool, ["arguments"] = arguments },
            };

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(deadline);
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(envelope.ToJsonString(), Encoding.UTF8, "application/json"),
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", ProtocolVersion);

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            status = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                // Keep the body too: on a 4xx it is chat's refusal text, the only place the reason lives.
                string? errorBody;
                try
                {
                    errorBody = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // Deadline or transport failure during the read: the status alone still classifies the failure.
                    return new ToolCall(null, "HTTP " + status + " " + response.ReasonPhrase + " (body unread: " + ex.Message + ")", status, null);
                }
                return new ToolCall(null, "HTTP " + status + " " + response.ReasonPhrase, status,
                    string.IsNullOrEmpty(errorBody) ? null : errorBody);
            }

            var media = response.Content.Headers.ContentType?.MediaType ?? "application/json";
            string? frameText;
            if (media.Contains("event-stream", StringComparison.OrdinalIgnoreCase))
            {
                var stream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
                await using (stream.ConfigureAwait(false))
                    frameText = await ReadLastSseDataAsync(stream, cts.Token).ConfigureAwait(false);
            }
            else
            {
                frameText = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            }

            if (string.IsNullOrWhiteSpace(frameText))
                return new ToolCall(null, "empty response body", status);
            var frame = JsonNode.Parse(frameText);
            var text = ReadToolText(frame);
            return text is null
                ? new ToolCall(null, DescribeToolFailure(frame), status, ReadToolErrorText(frame))
                : new ToolCall(text, null, status);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new ToolCall(null, ex.Message, status);
        }
    }

    /// <summary>Why a JSON-RPC frame carries no tool text: error frame, isError result, or missing text.
    /// Keeps failure detail instead of collapsing it to a boolean.</summary>
    private static string DescribeToolFailure(JsonNode? frame)
    {
        if (frame is not JsonObject obj) return "unparseable response frame";
        if (obj["error"] is { } err)
            return "JSON-RPC error: " + (ReadString(err["message"]) ?? err.ToJsonString());
        if (obj["result"] is JsonObject result)
        {
            if (result["isError"] is JsonValue flag && flag.TryGetValue<bool>(out var isError) && isError)
                return "tool returned isError: " + (FirstContentText(result["content"]) ?? "(no text)");
            return "tool result has no text content";
        }
        return "response frame has no result";
    }

    /// <summary>The last data: event of an SSE body, which is the JSON-RPC response frame.</summary>
    private static async Task<string?> ReadLastSseDataAsync(Stream stream, CancellationToken cancellationToken)
    {
        string? last = null;
        await foreach (var data in McpSse.ReadDataAsync(stream, cancellationToken).ConfigureAwait(false))
        {
            if (string.IsNullOrWhiteSpace(data) || data == "[DONE]") continue;
            last = data;
        }
        return last;
    }

    /// <summary>The server's own words when a frame carries no tool text: the JSON-RPC error message, or the
    /// content text of an isError result (chat's exact refusal). Null otherwise. Kept as ToolCall.Body so a
    /// refused post keeps the text the server answered with.</summary>
    private static string? ReadToolErrorText(JsonNode? frame)
    {
        if (frame is not JsonObject obj) return null;
        if (obj["error"] is JsonObject rpcError)
            return ReadString(rpcError["message"]) ?? rpcError.ToJsonString();
        if (obj["result"] is not JsonObject result) return null;
        if (result["isError"] is not JsonValue flag || !flag.TryGetValue<bool>(out var isError) || !isError) return null;
        return FirstContentText(result["content"]);
    }

    /// <summary>result.content[0].text from a JSON-RPC frame. Null for an error frame or isError == true.</summary>
    private static string? ReadToolText(JsonNode? frame)
    {
        if (frame is not JsonObject obj) return null;
        if (obj.ContainsKey("error")) return null;
        if (obj["result"] is not JsonObject result) return null;
        if (result["isError"] is JsonValue flag && flag.TryGetValue<bool>(out var isError) && isError) return null;
        if (result["content"] is not JsonArray content || content.Count == 0) return null;
        if (content[0] is not JsonObject first) return null;
        return ReadString(first["text"]);
    }

    private static string? ReadString(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;

    /// <summary>The text of the first content item of a tool result, when present.</summary>
    private static string? FirstContentText(JsonNode? content) =>
        content is JsonArray arr && arr.Count > 0 && arr[0] is JsonObject first ? ReadString(first["text"]) : null;

    private static IReadOnlyList<string> ReadStrings(JsonNode? node)
    {
        if (node is not JsonArray arr) return [];
        var list = new List<string>(arr.Count);
        foreach (var item in arr)
            if (ReadString(item) is string s) list.Add(s);
        return list;
    }

    private static List<long> ReadLongs(JsonArray arr)
    {
        var list = new List<long>(arr.Count);
        foreach (var item in arr)
            if (ReadLong(item) is long id) list.Add(id);
        return list;
    }

    private static long? ReadLong(JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        if (value.TryGetValue<long>(out var l)) return l;
        if (value.TryGetValue<double>(out var d)) return (long)d;
        if (value.TryGetValue<string>(out var s) && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) return parsed;
        return null;
    }

    public void Dispose() => _http.Dispose();
}
