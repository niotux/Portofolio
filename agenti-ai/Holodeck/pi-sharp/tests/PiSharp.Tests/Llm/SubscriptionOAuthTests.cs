using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PiSharp.Llm.Auth;

namespace PiSharp.Tests.Llm;

public class SubscriptionOAuthTests
{
    [Fact]
    public async Task CodexExchangesDeviceAuthorizationWithVerifierAndRefreshesSameAccount()
    {
        using var fixture = new Fixture();
        fixture.Add(200, """{"device_auth_id":"device-secret","user_code":"USER-CODE","interval":"2"}""");
        fixture.Add(403, "pending without JSON");
        fixture.Add(200, """{"authorization_code":"code&secret","code_verifier":"verifier+secret"}""");
        fixture.Add(200, Tokens(Jwt("account-1"), "refresh-one"));
        fixture.Add(200, Tokens(Jwt("account-1"), "refresh-two"));
        var login = await fixture.Client.StartAsync(SubscriptionProvider.Codex);
        var credential = await fixture.Client.CompleteAsync(login);
        Assert.Equal("account-1", credential.AccountId);
        Assert.Equal("refresh-one", credential.RefreshToken);
        Assert.Equal(new[] { 2d }, fixture.Delays);
        Assert.Equal("https://auth.openai.com/api/accounts/deviceauth/token", fixture.Requests[1].Url);
        Assert.Equal("device-secret", JsonNode.Parse(fixture.Requests[1].Body)!["device_auth_id"]!.GetValue<string>());
        var exchange = fixture.Requests[3];
        Assert.Equal("https://auth.openai.com/oauth/token", exchange.Url);
        Assert.Equal("application/x-www-form-urlencoded", exchange.ContentType);
        Assert.Contains("code=code%26secret", exchange.Body);
        Assert.Contains("code_verifier=verifier%2Bsecret", exchange.Body);
        Assert.Contains("redirect_uri=https%3A%2F%2Fauth.openai.com%2Fdeviceauth%2Fcallback", exchange.Body);
        var refreshed = await fixture.Client.RefreshAsync(credential);
        Assert.Equal("refresh-two", refreshed.RefreshToken);
        Assert.Contains("refresh_token=refresh-one", fixture.Requests[4].Body);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Client.CompleteAsync(login));
    }

    [Fact]
    public async Task GrokWaitsBeforePollingBacksOffAndPreservesUnrotatedRefreshToken()
    {
        using var fixture = new Fixture();
        fixture.Add(200, XaiDevice());
        fixture.Add(400, """{"error":"authorization_pending"}""");
        fixture.Add(400, """{"error":"slow_down"}""");
        fixture.Add(400, """{"error":"slow_down","interval":12}""");
        fixture.Add(200, Tokens("access-secret", "refresh-secret"));
        fixture.Add(200, """{"access_token":"new-access"}""");
        var login = await fixture.Client.StartAsync(SubscriptionProvider.Xai);
        var credential = await fixture.Client.CompleteAsync(login);
        Assert.Equal(new[] { 5d, 5d, 10d, 12d }, fixture.Delays);
        Assert.Equal(fixture.Now.AddSeconds(3300), credential.RefreshAfter);
        Assert.Contains("referrer=pi-sharp", fixture.Requests[0].Body);
        Assert.Contains("device_code=device-secret", fixture.Requests[1].Body);
        Assert.All(fixture.Requests.Skip(1), r => Assert.Equal("https://auth.x.ai/oauth2/token", r.Url));
        var refreshed = await fixture.Client.RefreshAsync(credential);
        Assert.Equal("refresh-secret", refreshed.RefreshToken);
        Assert.Equal("new-access", refreshed.AccessToken);
    }

    [Fact]
    public async Task PublicPayloadAndDiagnosticStringsDoNotExposeSecrets()
    {
        using var fixture = new Fixture();
        fixture.Add(200, XaiDevice());
        fixture.Add(200, Tokens("access-secret", "refresh-secret"));
        var login = await fixture.Client.StartAsync(SubscriptionProvider.Xai);
        var credential = await fixture.Client.CompleteAsync(login);
        var serialized = JsonSerializer.Serialize(login) + JsonSerializer.Serialize(credential)
            + login + credential;
        Assert.DoesNotContain("device-secret", serialized);
        Assert.DoesNotContain("access-secret", serialized);
        Assert.DoesNotContain("refresh-secret", serialized);
        Assert.Contains("USER-CODE", serialized);
    }

    [Theory]
    [InlineData("access_denied")]
    [InlineData("authorization_denied")]
    [InlineData("expired_token")]
    [InlineData("unknown-error-with-token-secret")]
    public async Task TerminalFailureStopsPollingAndDoesNotEchoProviderResponse(string error)
    {
        using var fixture = new Fixture();
        fixture.Add(200, XaiDevice());
        fixture.Add(400, new JsonObject { ["error"] = error, ["error_description"] = "token-secret" }.ToJsonString());
        var login = await fixture.Client.StartAsync(SubscriptionProvider.Xai);
        var exception = await Assert.ThrowsAsync<SubscriptionOAuthException>(() => fixture.Client.CompleteAsync(login));
        Assert.DoesNotContain("token-secret", exception.ToString());
        Assert.Equal(2, fixture.Requests.Count);
    }

    [Fact]
    public async Task ExpiredDeviceNeverPollsAndCancelledLoginNeverStartsPolling()
    {
        using var fixture = new Fixture();
        fixture.Add(200, XaiDevice(expires: 3));
        var login = await fixture.Client.StartAsync(SubscriptionProvider.Xai);
        var exception = await Assert.ThrowsAsync<SubscriptionOAuthException>(() => fixture.Client.CompleteAsync(login));
        Assert.Contains("expired", exception.Message);
        Assert.Single(fixture.Requests);
        Assert.Equal(new[] { 3d }, fixture.Delays);

        fixture.Add(200, XaiDevice());
        var cancelled = await fixture.Client.StartAsync(SubscriptionProvider.Xai);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Client.CompleteAsync(cancelled, cancellation.Token));
        Assert.Equal(2, fixture.Requests.Count);
    }

    [Fact]
    public async Task CancellationDuringWaitStopsBeforeSendingTokenRequest()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        fixture.OnDelay = () => cancellation.Cancel();
        fixture.Add(200, XaiDevice());
        var login = await fixture.Client.StartAsync(SubscriptionProvider.Xai);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Client.CompleteAsync(login, cancellation.Token));
        Assert.Single(fixture.Requests);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("http://auth.x.ai/device")]
    [InlineData("https://user:password@auth.x.ai/device")]
    public async Task RejectsUnsafeBrowserUrls(string url)
    {
        using var fixture = new Fixture();
        var body = JsonNode.Parse(XaiDevice())!.AsObject();
        body["verification_uri_complete"] = url;
        fixture.Add(200, body.ToJsonString());
        await Assert.ThrowsAsync<SubscriptionOAuthException>(() => fixture.Client.StartAsync(SubscriptionProvider.Xai));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("not-json-token-secret")]
    [InlineData("{\"device_auth_id\":\"secret\",\"user_code\":\"CODE\",\"interval\":-1}")]
    public async Task RejectsMalformedAuthorizationResponsesWithoutEchoingThem(string body)
    {
        using var fixture = new Fixture();
        fixture.Add(200, body);
        var exception = await Assert.ThrowsAsync<SubscriptionOAuthException>(() => fixture.Client.StartAsync(SubscriptionProvider.Codex));
        Assert.DoesNotContain("secret", exception.ToString());
    }

    [Fact]
    public async Task CodexRejectsRefreshThatChangesAccount()
    {
        using var fixture = new Fixture();
        fixture.Add(200, Tokens(Jwt("other-account"), "refresh-new"));
        var old = new SubscriptionCredential(SubscriptionProvider.Codex, Jwt("account-1"), "refresh-old", fixture.Now, "account-1");
        var error = await Assert.ThrowsAsync<SubscriptionOAuthException>(() => fixture.Client.RefreshAsync(old));
        Assert.Contains("does not match", error.Message);
    }

    [Fact]
    public async Task RevokedRefreshRequiresReconnectWithoutFallback()
    {
        using var fixture = new Fixture();
        fixture.Add(400, """{"error":"invalid_grant","error_description":"refresh-secret"}""");
        var old = new SubscriptionCredential(SubscriptionProvider.Xai, "access", "refresh-secret", fixture.Now, null);
        var error = await Assert.ThrowsAsync<SubscriptionOAuthException>(() => fixture.Client.RefreshAsync(old));
        Assert.Contains("Reconnect", error.Message);
        Assert.DoesNotContain("refresh-secret", error.ToString());
        Assert.Single(fixture.Requests);
    }

    [Fact]
    public async Task ConcurrentPollingOfSameLoginIsRejected()
    {
        using var fixture = new Fixture();
        fixture.Add(200, XaiDevice());
        using var cancellation = new CancellationTokenSource();
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new SubscriptionOAuth(fixture.Http, () => fixture.Now, async (_, ct) =>
        {
            waiting.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        });
        var login = await client.StartAsync(SubscriptionProvider.Xai);
        var first = client.CompleteAsync(login, cancellation.Token);
        await waiting.Task;
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.CompleteAsync(login));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.Single(fixture.Requests);
    }

    private static string XaiDevice(int expires = 900) => new JsonObject
    {
        ["device_code"] = "device-secret", ["user_code"] = "USER-CODE",
        ["verification_uri"] = "https://auth.x.ai/device", ["expires_in"] = expires,
    }.ToJsonString();

    private static string Tokens(string access, string refresh) => new JsonObject
    {
        ["access_token"] = access, ["refresh_token"] = refresh, ["expires_in"] = 3600,
    }.ToJsonString();

    private static string Jwt(string account) => "header." + Convert.ToBase64String(Encoding.UTF8.GetBytes(
        new JsonObject { ["https://api.openai.com/auth"] = new JsonObject { ["chatgpt_account_id"] = account } }.ToJsonString()))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".signature";

    private sealed class Fixture : HttpMessageHandler
    {
        public DateTimeOffset Now = new(2026, 9, 25, 0, 0, 0, TimeSpan.Zero);
        public List<double> Delays { get; } = [];
        public List<(string Url, string Body, string? ContentType)> Requests { get; } = [];
        private readonly Queue<(int Status, string Body)> _responses = [];
        public HttpClient Http { get; }
        public SubscriptionOAuth Client { get; }
        public Action? OnDelay { get; set; }

        public Fixture()
        {
            Http = new HttpClient(this, disposeHandler: false);
            Client = new SubscriptionOAuth(Http, () => Now, (duration, ct) =>
            {
                OnDelay?.Invoke();
                ct.ThrowIfCancellationRequested();
                Delays.Add(duration.TotalSeconds);
                Now += duration;
                return Task.CompletedTask;
            });
        }

        public void Add(int status, string body) => _responses.Enqueue((status, body));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Assert.Null(request.Headers.Authorization);
            Requests.Add((request.RequestUri!.AbsoluteUri, await request.Content!.ReadAsStringAsync(ct),
                request.Content.Headers.ContentType?.MediaType));
            var (status, body) = _responses.Dequeue();
            return new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body) };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Http.Dispose();
            base.Dispose(disposing);
        }
    }
}
