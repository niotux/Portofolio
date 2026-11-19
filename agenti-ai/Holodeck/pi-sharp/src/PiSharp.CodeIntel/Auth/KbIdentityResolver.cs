// Seat identity resolution. The KB stays the single source of identity: every protected request
// carries the seat's bearer token, and we ask the KB's /whoami about it, exactly like
// AiFactory.Chat's KbIdentityResolver. Valid answers are cached 60s, invalid ones 10s, so a
// revoked seat is forgotten quickly while live seats do not pay for a KB round trip per call.
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace PiSharp.CodeIntel.Auth;

/// <summary>The caller, as the knowledge base knows them.</summary>
public sealed record CodeIntelActor(string Slug, string Name, string[] WritableKinds, string? Runtime)
{
    public bool IsAdmin => WritableKinds.Contains("*", StringComparer.Ordinal);
}

/// <summary>Resolves bearer tokens by asking the KB's /whoami; the KB stays the single source of identity.</summary>
public sealed class KbIdentityResolver : IAsyncDisposable
{
    public const string ClientName = "kb";

    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan NegativeTtl = TimeSpan.FromSeconds(10);
    private readonly ConcurrentDictionary<string, (CodeIntelActor? Actor, DateTime Expires)> _cache = new();
    private readonly HttpClient _http;
    private readonly string _kbUrl;
    private readonly IOptions<CodeIntelOptions> _options;

    public KbIdentityResolver(HttpClient http, IOptions<CodeIntelOptions> options)
    {
        _http = http;
        _options = options;
        _kbUrl = options.Value.KbUrl;
    }

    public async Task<CodeIntelActor?> ResolveAsync(string token, CancellationToken ct)
    {
        var key = Hash(token);
        if (_cache.TryGetValue(key, out var hit) && hit.Expires > DateTime.UtcNow) return hit.Actor;

        using var req = new HttpRequestMessage(HttpMethod.Get, $"{_kbUrl}/whoami");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var res = await _http.SendAsync(req, ct);
        if (res.StatusCode == HttpStatusCode.Unauthorized)
        {
            _cache[key] = (null, DateTime.UtcNow + NegativeTtl);
            return null;
        }
        res.EnsureSuccessStatusCode();
        var who = await res.Content.ReadFromJsonAsync<WhoAmI>(JsonSerializerOptions.Web, ct)
                  ?? throw new InvalidOperationException("KB returned an empty identity.");
        var actor = new CodeIntelActor(who.Slug, who.Name, who.WritableKinds ?? [], who.Runtime);
        _cache[key] = (actor, DateTime.UtcNow + Ttl);
        TrimStale();
        return actor;
    }

    /// <summary>Unit-test seam: resolve without touching the network, by injecting the /whoami answer.</summary>
    internal Task<CodeIntelActor?> ResolveForTestAsync(string token, Func<CodeIntelActor?> source, CancellationToken ct)
    {
        var key = Hash(token);
        if (_cache.TryGetValue(key, out var hit) && hit.Expires > DateTime.UtcNow) return Task.FromResult(hit.Actor);
        var actor = source();
        _cache[key] = (actor, DateTime.UtcNow + (actor is null ? NegativeTtl : Ttl));
        TrimStale();
        return Task.FromResult(actor);
    }

    private void TrimStale()
    {
        if (_cache.Count <= 1000) return;
        foreach (var stale in _cache.Where(kv => kv.Value.Expires < DateTime.UtcNow).ToList())
            _cache.TryRemove(stale.Key, out _);
    }

    private static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private sealed record WhoAmI(string Slug, string Name, string[]? WritableKinds, string? Runtime);
}

/// <summary>Extracts the bearer token from an Authorization header.</summary>
public static class BearerTokens
{
    public static string? FromHeader(string? authorization) =>
        authorization is not null && authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authorization[7..].Trim() is { Length: > 0 } t ? t : null
            : null;
}
