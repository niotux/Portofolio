using System.Net;
using System.Text.Json;
using PiSharp.Llm.Auth;
using PiSharp.Tests.Tools;

namespace PiSharp.Tests.Llm;

public class SubscriptionConnectionStoreTests
{
    [Fact]
    public async Task TwoStoresSerializeRotationAndPersistBeforeReturning()
    {
        using var tmp = new TempWorkspace();
        var calls = 0;
        using var handler = new CeilingFakeHandler(async req =>
        {
            Assert.Contains("refresh_token=old-refresh", await req.Content!.ReadAsStringAsync());
            Interlocked.Increment(ref calls);
            await Task.Delay(75);
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("""{"access_token":"new-access","refresh_token":"rotated","expires_in":3600}""") };
        });
        using var http = new HttpClient(handler);
        var first = new SubscriptionConnectionStore(tmp.Path, new(http));
        var second = new SubscriptionConnectionStore(tmp.Path, new(http));
        await first.SaveAsync("grok", "Personal Grok", new(SubscriptionProvider.Xai, "old-access", "old-refresh", DateTimeOffset.UtcNow.AddMinutes(-1), null));
        var values = await Task.WhenAll(first.GetAsync("grok", SubscriptionProvider.Xai, default), second.GetAsync("grok", SubscriptionProvider.Xai, default));
        Assert.Equal(1, calls);
        Assert.All(values, v => Assert.Equal("rotated", v.RefreshToken));
        var listed = JsonSerializer.Serialize(await first.ListAsync());
        Assert.DoesNotContain("rotated", listed);
        Assert.DoesNotContain("new-access", listed);
        Assert.Contains("Personal Grok", listed);
        Assert.Empty(Directory.GetFiles(tmp.Path, "*.tmp"));
        await second.DeleteAsync("grok");
        await Assert.ThrowsAsync<SubscriptionOAuthException>(() => first.GetAsync("grok", SubscriptionProvider.Xai, default));
    }

    [Fact]
    public async Task FailedRefreshDoesNotReplaceCredentialAndProviderMismatchNeverUsesIt()
    {
        using var tmp = new TempWorkspace();
        using var http = new HttpClient(new CeilingFakeHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
        { Content = new StringContent("""{"error":"invalid_grant"}""") })));
        var store = new SubscriptionConnectionStore(tmp.Path, new(http));
        await store.SaveAsync("grok", "Grok", new(SubscriptionProvider.Xai, "old", "refresh", DateTimeOffset.UtcNow.AddMinutes(-1), null));
        var before = await File.ReadAllTextAsync(Path.Combine(tmp.Path, "grok.json"));
        await Assert.ThrowsAsync<SubscriptionOAuthException>(() => store.GetAsync("grok", SubscriptionProvider.Xai, default));
        Assert.Equal(before, await File.ReadAllTextAsync(Path.Combine(tmp.Path, "grok.json")));
        await Assert.ThrowsAsync<SubscriptionOAuthException>(() => store.GetAsync("grok", SubscriptionProvider.Codex, default));
        await Assert.ThrowsAsync<ArgumentException>(() => store.DeleteAsync("../grok"));
    }
}
