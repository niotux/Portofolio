using System.Text.Json.Nodes;
using PiSharp.Core.Harness;

namespace PiSharp.Tests.Harness;

public class CubicleClientTests
{
    private static JsonArray OneEvent() =>
    [
        (JsonNode)new JsonObject { ["seq"] = 1, ["run_id"] = "r1", ["type"] = "prompt", ["payload"] = new JsonObject { ["text"] = "hi" } },
    ];

    [Fact]
    public async Task PostSendsBearerAndBatch_TrueOn202()
    {
        using var stub = new CubicleStub();
        using var client = new CubicleClient(stub.Url + "/", "secret");
        Assert.True(await client.PostAsync(OneEvent()));
        var req = Assert.Single(stub.Requests);
        Assert.Equal("POST", req.Method);
        Assert.Equal("/cubicle", req.PathAndQuery);
        Assert.Equal("Bearer secret", req.Authorization);
        Assert.Single(req.Events);
        Assert.Equal("prompt", req.Events[0]!["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task PostWithoutTokenSendsNoAuthorization()
    {
        using var stub = new CubicleStub();
        using var client = new CubicleClient(stub.Url, null);
        Assert.True(await client.PostAsync(OneEvent()));
        Assert.Null(Assert.Single(stub.Requests).Authorization);
    }

    [Fact]
    public async Task PostFalseOn404()
    {
        using var stub = new CubicleStub();
        stub.Handler = _ => Task.FromResult(new StubResponse(404, "{}"));
        using var client = new CubicleClient(stub.Url, "t");
        Assert.False(await client.PostAsync(OneEvent()));
    }

    [Fact]
    public async Task PostFalseOnConnectionReset()
    {
        using var stub = new CubicleStub();
        stub.Handler = _ => Task.FromResult(StubResponse.Reset);
        using var client = new CubicleClient(stub.Url, "t");
        Assert.False(await client.PostAsync(OneEvent()));
    }

    [Fact]
    public async Task PostFalseWhenNothingListens()
    {
        using var stub = new CubicleStub();
        var url = stub.Url;
        stub.Dispose();
        using var client = new CubicleClient(url, "t");
        Assert.False(await client.PostAsync(OneEvent()));
    }

    [Fact]
    public async Task PostFalseOnTimeout()
    {
        using var stub = new CubicleStub();
        stub.Handler = _ => Task.FromResult(StubResponse.Accepted() with { Delay = TimeSpan.FromSeconds(3) });
        using var client = new CubicleClient(stub.Url, "t", timeout: TimeSpan.FromMilliseconds(200));
        Assert.False(await client.PostAsync(OneEvent()));
    }

    [Fact]
    public async Task GetParsesMessagesAndCursor()
    {
        using var stub = new CubicleStub();
        stub.Handler = _ => Task.FromResult(StubResponse.Steering(12, (11, "digital-scribe", "slow down"), (12, "architect-1", "stop")));
        using var client = new CubicleClient(stub.Url, "t");
        var reply = await client.GetAsync(7, 3);
        Assert.NotNull(reply);
        Assert.Equal(12, reply.Last);
        Assert.Equal(2, reply.Messages.Count);
        Assert.Equal(11, reply.Messages[0].Id);
        Assert.Equal("digital-scribe", reply.Messages[0].From);
        Assert.Equal("slow down", reply.Messages[0].Text);
        var req = Assert.Single(stub.Requests);
        Assert.Equal("GET", req.Method);
        Assert.StartsWith("/cubicle?", req.PathAndQuery, StringComparison.Ordinal);
        Assert.Equal(7, req.Since);
        Assert.Equal(3, req.Wait);
    }

    [Fact]
    public async Task GetClampsWaitAndReturnsNullOnFailure()
    {
        using var stub = new CubicleStub();
        stub.Handler = _ => Task.FromResult(new StubResponse(503, "busy"));
        using var client = new CubicleClient(stub.Url, "t");
        Assert.Null(await client.GetAsync(0, 500));
        Assert.Equal(CubicleClient.MaxWaitSeconds, Assert.Single(stub.Requests).Wait);
    }

    [Fact]
    public async Task GetNullOnConnectionReset()
    {
        using var stub = new CubicleStub();
        stub.Handler = _ => Task.FromResult(StubResponse.Reset);
        using var client = new CubicleClient(stub.Url, "t");
        Assert.Null(await client.GetAsync(0, 0));
    }

    [Fact]
    public void ParseTreatsGarbageAsUnavailable()
    {
        Assert.Null(CubicleClient.Parse("[1,2]", 0));
        var reply = CubicleClient.Parse("""{"messages":[{"from":"x","text":"no id"},{"id":"9","from":"y","text":"t"}]}""", 4);
        Assert.NotNull(reply);
        Assert.Equal(4, reply.Last);
        Assert.Equal(9, Assert.Single(reply.Messages).Id);
    }
}
