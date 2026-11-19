using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace PiSharp.Core.Notebook;

/// <summary>Ordered durable outbox replay. Ambiguous writes remain pending and replay the same operation ID.
/// A conflict stops the queue; later edits never silently skip a failed predecessor.</summary>
public sealed class NotebookSync : IDisposable
{
    private readonly HttpClient _http;
    private long? _generation;
    private bool _restored;
    public ArtifactStore? Artifacts { get; set; }
    /// <summary>Told the ID of an artifact set aside because its bytes no longer match its name (for the seat's log).</summary>
    public Action<string>? SetAside { get; set; }
    public NotebookSync(string kbUrl, string token, HttpMessageHandler? handler = null)
    {
        var endpoint = new Uri(kbUrl);
        _http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false })
        { BaseAddress = new Uri(endpoint.GetLeftPart(UriPartial.Authority)), Timeout = TimeSpan.FromSeconds(10), MaxResponseContentBufferSize = 4 * 1024 * 1024 };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
    public async Task FlushAsync(NotebookStore store, CancellationToken ct)
    {
        if (_generation is null)
        {
            using var response = await _http.GetAsync("/notebook", ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var identity = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false))!;
            if (identity["owner"]?.GetValue<string>() != store.Owner) throw new InvalidOperationException("Notebook server owner mismatch.");
            if (identity["protocolVersion"]?.GetValue<int>() != 1) throw new InvalidOperationException("Incompatible notebook protocol; local operations remain pending.");
            _generation = identity["generation"]!.GetValue<long>();
        }
        if (!_restored && store.PendingCount == 0)
        {
            var cursor = "";
            while (true)
            {
                using var response = await _http.GetAsync("/notebook/records?after=" + Uri.EscapeDataString(cursor), ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var rows = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false))!.AsArray();
                store.ImportCommitted([.. rows.Select(row => row!.AsObject())]);
                if (rows.Count == 0) break;
                var next = rows[^1]!["id"]!.GetValue<string>();
                if (string.CompareOrdinal(next, cursor) <= 0) throw new InvalidOperationException("Notebook pagination did not advance.");
                cursor = next;
            }
            _restored = true;
        }
        foreach (var operation in store.Pending(100))
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/notebook/operations");
            request.Headers.Add("X-Notebook-Generation", _generation.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            request.Content = new StringContent(operation.ToJsonString(), Encoding.UTF8, "application/json");
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            await EnsureAcceptedAsync(response, OperationName(operation), ct).ConfigureAwait(false);
            var receipt = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false))!;
            var operationId = operation["operationId"]!.GetValue<string>();
            if (receipt["operationId"]?.GetValue<string>() != operationId || receipt["committed"]?.GetValue<bool>() != true
                || receipt["revision"]?.GetValue<long>() != operation["record"]!["revision"]!.GetValue<long>())
                throw new InvalidOperationException("Invalid notebook commit receipt.");
            store.Acknowledge(operationId);
        }
        if (Artifacts is { } artifacts)
        {
            foreach (var artifact in artifacts.PendingUploads().Take(4))
            {
                // A file whose bytes no longer hash to its name is refused by the KB for good: set it aside, not offered again.
                if (!artifacts.Intact(artifact.Id))
                {
                    artifacts.SetAside(artifact.Id);
                    SetAside?.Invoke(artifact.Id);
                    continue;
                }
                using var request = new HttpRequestMessage(HttpMethod.Put, "/notebook/artifacts/" + artifact.Id);
                request.Headers.Add("X-Notebook-Generation", _generation.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                request.Content = new StreamContent(File.OpenRead(artifact.Path));
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
                await EnsureAcceptedAsync(response, $"artifact {artifact.Id[..Math.Min(12, artifact.Id.Length)]}", ct).ConfigureAwait(false);
                var receipt = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
                if (receipt?["committed"]?.GetValue<bool>() != true || receipt["sha256"]?.GetValue<string>() != artifact.Id)
                    throw new InvalidOperationException("Invalid artifact commit receipt.");
                artifacts.MarkUploaded(artifact.Id);
            }
        }
    }
    /// <summary>
    /// A refusal names the server's reason and what it refused. The outbox goes in order, so one refused operation holds
    /// every later one back; until 2026-10-30 the seat's log said only "400 (Bad Request)" on every turn, for hours, and
    /// nobody could tell which record or why (pi13).
    /// </summary>
    private static async Task EnsureAcceptedAsync(HttpResponseMessage response, string what, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        string reason;
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            reason = (JsonNode.Parse(body) as JsonObject)?["error"]?.GetValue<string>() ?? body;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or HttpRequestException) { reason = ""; }
        if (reason.Length > 300) reason = reason[..300];
        throw new HttpRequestException(
            $"{(int)response.StatusCode} ({response.ReasonPhrase}) for {what}{(reason.Length > 0 ? ": " + reason : "")}", null, response.StatusCode);
    }

    /// <summary>"record plan-step-3 rev 4 (plan-step, 1834 bytes)" for an outbox operation.</summary>
    private static string OperationName(JsonObject operation)
    {
        var record = operation["record"] as JsonObject;
        return FormattableString.Invariant(
            $"record {record?["id"]?.GetValue<string>() ?? "?"} rev {record?["revision"]?.ToJsonString() ?? "?"} ({record?["kind"]?.GetValue<string>() ?? "?"}, {Encoding.UTF8.GetByteCount(operation.ToJsonString())} bytes)");
    }

    public void Dispose() => _http.Dispose();
}
