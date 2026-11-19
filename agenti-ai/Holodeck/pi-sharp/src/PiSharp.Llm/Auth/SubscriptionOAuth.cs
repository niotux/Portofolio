// Ported from pi packages/ai/src/auth/oauth/{openai-codex,xai,device-code}.ts @ 71dca871b.
// Device-code flows run on the host; only DeviceLoginInfo belongs in the browser.
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace PiSharp.Llm.Auth;

public enum SubscriptionProvider { Codex, Xai }

/// <summary>Safe UI payload. Device codes used for token exchange never leave the server.</summary>
public sealed record DeviceLoginInfo(SubscriptionProvider Provider, string UserCode,
    Uri VerificationUri, DateTimeOffset ExpiresAt);

/// <summary>Server-owned, single-use login attempt. Never persist or expose the device secret.</summary>
public sealed class DeviceLogin
{
    public DeviceLoginInfo Info { get; }
    internal string Secret { get; }
    internal TimeSpan Interval { get; }
    private int _started;

    internal DeviceLogin(DeviceLoginInfo info, string secret, TimeSpan interval)
        => (Info, Secret, Interval) = (info, secret, interval);

    internal void Claim()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("This login attempt has already been polled. Start a new login.");
    }
}

/// <summary>OAuth credentials, never an API-key fallback. Store only in a server-owned secret store.</summary>
public sealed class SubscriptionCredential
{
    public SubscriptionProvider Provider { get; }
    [JsonIgnore] public string AccessToken { get; }
    [JsonIgnore] public string RefreshToken { get; }
    public DateTimeOffset RefreshAfter { get; }
    public string? AccountId { get; }

    internal SubscriptionCredential(SubscriptionProvider provider, string accessToken, string refreshToken,
        DateTimeOffset refreshAfter, string? accountId)
        => (Provider, AccessToken, RefreshToken, RefreshAfter, AccountId)
            = (provider, accessToken, refreshToken, refreshAfter, accountId);
}

/// <summary>Failures contain no response bodies, authorization codes, or token values.</summary>
public sealed class SubscriptionOAuthException(string message) : Exception(message);

/// <summary>
/// Direct subscription OAuth port. Caller owns HttpClient and must disable automatic redirects.
/// Login does not prove subscription entitlement; the inference adapter must surface limits without fallback.
/// </summary>
public sealed class SubscriptionOAuth
{
    private const string CodexClientId = "app_EMoamEEZ73f0CkXaXp7hrann";
    private const string XaiClientId = "b1a00492-073a-47ea-816f-4c329264a828";
    private const string CodexTokenUrl = "https://auth.openai.com/oauth/token";
    private const string XaiTokenUrl = "https://auth.x.ai/oauth2/token";
    private const string CodexRedirect = "https://auth.openai.com/deviceauth/callback";
    private readonly HttpClient _http;
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public SubscriptionOAuth(HttpClient http) : this(http, () => DateTimeOffset.UtcNow, Task.Delay) { }

    internal SubscriptionOAuth(HttpClient http, Func<DateTimeOffset> now,
        Func<TimeSpan, CancellationToken, Task> delay)
        => (_http, _now, _delay) = (http, now, delay);

    public async Task<DeviceLogin> StartAsync(SubscriptionProvider provider, CancellationToken ct = default)
    {
        ValidateProvider(provider);
        var codex = provider == SubscriptionProvider.Codex;
        var response = codex
            ? await PostJsonAsync("https://auth.openai.com/api/accounts/deviceauth/usercode",
                new JsonObject { ["client_id"] = CodexClientId }, ct).ConfigureAwait(false)
            : await PostFormAsync("https://auth.x.ai/oauth2/device/code", new()
            {
                ["client_id"] = XaiClientId,
                ["scope"] = "openid profile email offline_access grok-cli:access api:access",
                ["referrer"] = "pi-sharp",
            }, ct).ConfigureAwait(false);
        if (codex && response.Status == HttpStatusCode.NotFound)
            throw new SubscriptionOAuthException("Codex device login is not enabled for this account/server.");
        EnsureSuccess(response, "Device authorization");
        var body = response.Body;
        var interval = Number(body["interval"], allowString: codex);
        if (codex && (interval is null || interval < 0)) throw InvalidResponse();
        var lifetime = codex ? 900 : PositiveNumber(body, "expires_in");
        var uri = codex ? new Uri("https://auth.openai.com/codex/device")
            : VerificationUri(body);
        return new DeviceLogin(new(provider, Required(body, "user_code"), uri, AddSeconds(lifetime)),
            Required(body, codex ? "device_auth_id" : "device_code"),
            Seconds(Math.Max(1, interval is > 0 ? interval.Value : codex ? 1 : 5)));
    }

    public async Task<SubscriptionCredential> CompleteAsync(DeviceLogin login, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        login.Claim();
        var codex = login.Info.Provider == SubscriptionProvider.Codex;
        var interval = login.Interval;
        var remaining = login.Info.ExpiresAt - _now();
        if (remaining <= TimeSpan.Zero) throw Expired();
        // Also bound a stalled HTTP request by the device-code deadline.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(remaining);
        var token = deadline.Token;
        try
        {
            if (!codex) await WaitAsync(interval, login.Info.ExpiresAt, token).ConfigureAwait(false);
            while (_now() < login.Info.ExpiresAt)
            {
                token.ThrowIfCancellationRequested();
                var response = codex
                    ? await PostJsonAsync("https://auth.openai.com/api/accounts/deviceauth/token", new()
                    {
                        ["device_auth_id"] = login.Secret, ["user_code"] = login.Info.UserCode,
                    }, token).ConfigureAwait(false)
                    : await PostFormAsync(XaiTokenUrl, new()
                    {
                        ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                        ["client_id"] = XaiClientId, ["device_code"] = login.Secret,
                    }, token).ConfigureAwait(false);
                if (_now() >= login.Info.ExpiresAt) throw Expired();
                if (response.Success)
                {
                    if (codex)
                    {
                        response = await PostFormAsync(CodexTokenUrl, new()
                        {
                            ["grant_type"] = "authorization_code", ["client_id"] = CodexClientId,
                            ["code"] = Required(response.Body, "authorization_code"),
                            ["code_verifier"] = Required(response.Body, "code_verifier"),
                            ["redirect_uri"] = CodexRedirect,
                        }, token).ConfigureAwait(false);
                        EnsureSuccess(response, "Token exchange");
                    }
                    token.ThrowIfCancellationRequested();
                    return ReadCredential(login.Info.Provider, response.Body);
                }
                var error = ErrorCode(response.Body);
                if (codex && response.Status is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
                {
                    // Codex signals authorization pending with either status, even without JSON.
                }
                else if (error == "slow_down")
                {
                    var minimum = Number(response.Body["interval"]);
                    interval = minimum is > 0 ? Seconds(Math.Max(1, minimum.Value)) : interval + TimeSpan.FromSeconds(5);
                }
                else if (error != (codex ? "deviceauth_authorization_pending" : "authorization_pending"))
                {
                    if (error is "access_denied" or "authorization_denied")
                        throw new SubscriptionOAuthException("Device authorization was denied.");
                    if (error == "expired_token") throw Expired();
                    EnsureSuccess(response, "Device polling");
                }
                await WaitAsync(interval, login.Info.ExpiresAt, token).ConfigureAwait(false);
            }
            throw Expired();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            throw Expired();
        }
    }

    /// <summary>Caller must serialize refresh and atomically persist rotated tokens per connection.</summary>
    public async Task<SubscriptionCredential> RefreshAsync(SubscriptionCredential credential, CancellationToken ct = default)
    {
        ValidateProvider(credential.Provider);
        var codex = credential.Provider == SubscriptionProvider.Codex;
        var response = await PostFormAsync(codex ? CodexTokenUrl : XaiTokenUrl, new()
        {
            ["grant_type"] = "refresh_token", ["client_id"] = codex ? CodexClientId : XaiClientId,
            ["refresh_token"] = credential.RefreshToken,
        }, ct).ConfigureAwait(false);
        if (ErrorCode(response.Body) == "invalid_grant")
            throw new SubscriptionOAuthException("Subscription login expired or was revoked. Reconnect this account.");
        EnsureSuccess(response, "Token refresh");
        var updated = ReadCredential(credential.Provider, response.Body, credential.RefreshToken);
        if (codex && updated.AccountId != credential.AccountId)
            throw new SubscriptionOAuthException("Refreshed Codex account does not match the connected account.");
        return updated;
    }

    private SubscriptionCredential ReadCredential(SubscriptionProvider provider, JsonObject body, string? oldRefresh = null)
    {
        var access = Required(body, "access_token");
        var codex = provider == SubscriptionProvider.Codex;
        var refresh = !codex && !body.ContainsKey("refresh_token") && oldRefresh is not null
            ? oldRefresh : Required(body, "refresh_token");
        var lifetime = !codex && !body.ContainsKey("expires_in") ? 3600 : PositiveNumber(body, "expires_in");
        return new(provider, access, refresh, AddSeconds(lifetime - (codex ? 0 : 300)),
            codex ? CodexAccountId(access) : null);
    }

    private static string CodexAccountId(string access)
    {
        try
        {
            var parts = access.Split('.');
            if (parts.Length != 3) throw InvalidResponse();
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
            var body = JsonNode.Parse(Convert.FromBase64String(payload));
            // Routing metadata from the token endpoint, not independent JWT verification.
            if (body is JsonObject obj && obj["https://api.openai.com/auth"] is JsonObject auth)
                return Required(auth, "chatgpt_account_id");
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException) { }
        throw new SubscriptionOAuthException("Codex token has no valid account ID.");
    }

    private async Task WaitAsync(TimeSpan interval, DateTimeOffset expires, CancellationToken ct)
    {
        var remaining = expires - _now();
        if (remaining > TimeSpan.Zero)
            await _delay(interval < remaining ? interval : remaining, ct).ConfigureAwait(false);
    }

    private Task<OAuthResponse> PostJsonAsync(string url, JsonObject body, CancellationToken ct)
        => PostAsync(url, new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"), ct);

    private Task<OAuthResponse> PostFormAsync(string url, Dictionary<string, string> body, CancellationToken ct)
        => PostAsync(url, new FormUrlEncodedContent(body), ct);

    private async Task<OAuthResponse> PostAsync(string url, HttpContent content, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        JsonObject body;
        try { body = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false)) as JsonObject ?? new(); }
        catch (JsonException) { body = new(); }
        return new(response.StatusCode, body);
    }

    private sealed record OAuthResponse(HttpStatusCode Status, JsonObject Body)
    {
        public bool Success => (int)Status is >= 200 and < 300;
    }

    private static Uri VerificationUri(JsonObject body)
    {
        var basic = HttpsUri(Required(body, "verification_uri"));
        return Text(body["verification_uri_complete"]) is { Length: > 0 } complete ? HttpsUri(complete) : basic;
    }

    private static Uri HttpsUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0)
            throw new SubscriptionOAuthException("Invalid device verification URL.");
        return uri;
    }

    private DateTimeOffset AddSeconds(double seconds)
    {
        try { return _now().AddSeconds(seconds); }
        catch (ArgumentOutOfRangeException) { throw InvalidResponse(); }
    }

    private static TimeSpan Seconds(double seconds)
    {
        // CancelAfter and Task.Delay support at most uint.MaxValue - 1 milliseconds.
        if (seconds > (uint.MaxValue - 1) / 1000d) throw InvalidResponse();
        return TimeSpan.FromSeconds(seconds);
    }

    private static double PositiveNumber(JsonObject body, string key)
        => Number(body[key]) is > 0 and var n && n <= (uint.MaxValue - 1) / 1000d ? n : throw InvalidResponse();

    private static double? Number(JsonNode? node, bool allowString = false)
    {
        if (node is not JsonValue value) return null;
        if (value.TryGetValue<double>(out var n) && double.IsFinite(n)) return n;
        if (allowString && value.TryGetValue<string>(out var s)
            && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out n) && double.IsFinite(n)) return n;
        return null;
    }

    private static string? Text(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    private static string Required(JsonObject body, string key)
        => Text(body[key]) is { Length: > 0 } text ? text : throw InvalidResponse();
    private static string? ErrorCode(JsonObject body)
        => body["error"] is JsonObject error ? Text(error["code"]) : Text(body["error"]);
    private static void ValidateProvider(SubscriptionProvider provider)
    {
        if (provider is not (SubscriptionProvider.Codex or SubscriptionProvider.Xai))
            throw new ArgumentOutOfRangeException(nameof(provider));
    }
    private static void EnsureSuccess(OAuthResponse response, string operation)
    {
        if (!response.Success)
            throw new SubscriptionOAuthException($"{operation} failed (HTTP {(int)response.Status}).");
    }
    private static SubscriptionOAuthException InvalidResponse() => new("Invalid OAuth response fields.");
    private static SubscriptionOAuthException Expired() => new("Device login expired. Start a new login.");
}
