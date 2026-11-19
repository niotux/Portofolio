// HTTP transport for the AI Factory cubicle (chat server /cubicle endpoints). See docs adr-pi-sharp-cubicle-client.
// Contract: POST {url}/cubicle with a JSON array of trace events -> 202 {room,last};
//           GET  {url}/cubicle?since=<id>&wait=<0..90> -> 200 {messages:[{id,from,at,text}],last}.
// Every failure (non-2xx, timeout, connection error, bad JSON) is "unavailable": PostAsync returns false and
// GetAsync returns null. Nothing here throws into the agent loop.
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace PiSharp.Core.Harness;

public sealed record CubicleMessage(long Id, string From, string At, string Text);

public sealed record CubicleReply(IReadOnlyList<CubicleMessage> Messages, long Last);

public sealed class CubicleClient : IDisposable
{
    public const int MaxWaitSeconds = 90;
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    private readonly HttpClient _http;
    private readonly Uri _endpoint;
    private readonly TimeSpan _timeout;

    public CubicleClient(string url, string? token, HttpMessageHandler? handler = null, TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        _endpoint = new Uri(url.TrimEnd('/') + "/cubicle", UriKind.Absolute);
        _timeout = timeout ?? DefaultTimeout;
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: true);
        // Per-request cancellation carries the deadline so long-poll GETs can exceed the POST timeout.
        _http.Timeout = Timeout.InfiniteTimeSpan;
        if (!string.IsNullOrEmpty(token))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public Uri Endpoint => _endpoint;

    /// <summary>Posts one batch of trace events. True on any 2xx; false on anything else. Never throws.</summary>
    public async Task<bool> PostAsync(JsonArray events, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_timeout);
            using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
            {
                Content = new StringContent(events.ToJsonString(), Encoding.UTF8, "application/json"),
            };
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }
    }

    /// <summary>Reads steering messages after <paramref name="since"/>, long-polling up to <paramref name="waitSeconds"/>.
    /// Null when the server is unavailable. Never throws.</summary>
    public async Task<CubicleReply?> GetAsync(long since, int waitSeconds, CancellationToken cancellationToken = default)
    {
        waitSeconds = Math.Clamp(waitSeconds, 0, MaxWaitSeconds);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_timeout + TimeSpan.FromSeconds(waitSeconds));
            var uri = new Uri(_endpoint, FormattableString.Invariant($"?since={since}&wait={waitSeconds}"));
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            var body = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            return Parse(body, since);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    public static CubicleReply? Parse(string body, long since)
    {
        if (JsonNode.Parse(body) is not JsonObject obj) return null;
        var last = ReadLong(obj["last"]) ?? since;
        var messages = new List<CubicleMessage>();
        if (obj["messages"] is JsonArray arr)
        {
            foreach (var node in arr)
            {
                if (node is not JsonObject m) continue;
                var id = ReadLong(m["id"]);
                if (id is null) continue;
                messages.Add(new CubicleMessage(
                    id.Value,
                    m["from"]?.GetValue<string>() ?? "?",
                    m["at"]?.ToString() ?? "",
                    m["text"]?.GetValue<string>() ?? ""));
            }
        }
        return new CubicleReply(messages, last);
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
