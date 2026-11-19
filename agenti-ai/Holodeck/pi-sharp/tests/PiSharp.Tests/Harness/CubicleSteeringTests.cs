using PiSharp.Core.Harness;
using PiSharp.Core.Messages;

namespace PiSharp.Tests.Harness;

public class CubicleSteeringTests
{
    private sealed class FakeClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        public void Advance(TimeSpan by) => Now += by;
    }

    private static string Text(Message m) => Assert.IsType<UserMessage>(m).Text;

    [Fact]
    public async Task PollsWithCursorAndDedupesIds()
    {
        using var stub = new CubicleStub();
        var call = 0;
        stub.Handler = req => Task.FromResult(++call switch
        {
            1 => StubResponse.Steering(6, (5, "digital-scribe", "slow down"), (6, "architect-1", "stop")),
            _ => StubResponse.Steering(7, (6, "architect-1", "stop"), (7, "digital-scribe", "resume")),
        });
        using var client = new CubicleClient(stub.Url, "t");
        var steering = new CubicleSteering(client) { SkipHistory = false };

        var first = await steering.GetSteeringMessages();
        Assert.Equal(["[steering from @digital-scribe] slow down", "[steering from @architect-1] stop"], first.Select(Text));
        Assert.Equal(6, steering.Since);

        var second = await steering.GetSteeringMessages();
        Assert.Equal("[steering from @digital-scribe] resume", Text(Assert.Single(second)));
        Assert.Equal(7, steering.Since);

        var gets = stub.Gets.ToList();
        Assert.Equal(2, gets.Count);
        Assert.Equal(0, gets[0].Since);
        Assert.Equal(0, gets[0].Wait);
        Assert.Equal(6, gets[1].Since);
    }

    [Fact]
    public async Task SkipsHistoryBeforeFirstPollByDefault()
    {
        using var stub = new CubicleStub();
        var call = 0;
        stub.Handler = req => Task.FromResult(++call switch
        {
            1 => StubResponse.Steering(6, (5, "digital-scribe", "old note"), (6, "architect-1", "older note")),
            _ => StubResponse.Steering(7, (7, "digital-scribe", "fresh note")),
        });
        using var client = new CubicleClient(stub.Url, "t");
        var steering = new CubicleSteering(client);

        Assert.Empty(await steering.GetSteeringMessages());
        Assert.Equal(6, steering.Since);
        var second = await steering.GetSteeringMessages();
        Assert.Equal("[steering from @digital-scribe] fresh note", Text(Assert.Single(second)));
        Assert.Equal(6, stub.Gets.ToList()[1].Since);
    }

    [Fact]
    public async Task FollowUpsAreOffByDefaultAndLongPollWhenEnabled()
    {
        using var stub = new CubicleStub();
        using var client = new CubicleClient(stub.Url, "t");
        var off = new CubicleSteering(client);
        Assert.Empty(await off.GetFollowUpMessages());
        Assert.Empty(stub.Requests);

        var on = new CubicleSteering(client) { FollowUpWaitSeconds = 3 };
        Assert.Empty(await on.GetFollowUpMessages());
        var req = Assert.Single(stub.Gets);
        Assert.Equal(3, req.Wait);
    }

    [Fact]
    public async Task UnavailableServerBacksOffDoublingToCapAndResetsOnSuccess()
    {
        using var stub = new CubicleStub();
        var healthy = false;
        stub.Handler = req => Task.FromResult(healthy ? StubResponse.Steering(1, (1, "x", "go")) : new StubResponse(503, "down"));
        using var client = new CubicleClient(stub.Url, "t");
        var clock = new FakeClock();
        var steering = new CubicleSteering(client, clock) { SkipHistory = false };

        Assert.Empty(await steering.GetSteeringMessages());
        Assert.Equal(TimeSpan.FromSeconds(30), steering.Backoff);
        Assert.Single(stub.Requests);

        Assert.Empty(await steering.GetSteeringMessages()); // inside the back-off window: no request
        Assert.Single(stub.Requests);

        var expected = new[] { 60, 120, 240, 300, 300 };
        foreach (var seconds in expected)
        {
            clock.Advance(steering.Backoff + TimeSpan.FromSeconds(1));
            Assert.Empty(await steering.GetSteeringMessages());
            Assert.Equal(TimeSpan.FromSeconds(seconds), steering.Backoff);
        }
        Assert.Equal(1 + expected.Length, stub.Requests.Count);

        healthy = true;
        clock.Advance(TimeSpan.FromMinutes(6));
        var messages = await steering.GetSteeringMessages();
        Assert.Equal("[steering from @x] go", Text(Assert.Single(messages)));
        Assert.Equal(TimeSpan.Zero, steering.Backoff);

        Assert.Empty(await steering.GetSteeringMessages()); // healthy again: polls immediately
        Assert.Equal(3 + expected.Length, stub.Requests.Count);
    }

    [Fact]
    public async Task ConnectionResetAndClosedPortYieldEmpty()
    {
        using var stub = new CubicleStub();
        stub.Handler = _ => Task.FromResult(StubResponse.Reset);
        using var client = new CubicleClient(stub.Url, "t");
        var steering = new CubicleSteering(client);
        Assert.Empty(await steering.GetSteeringMessages());
        Assert.Equal(TimeSpan.FromSeconds(30), steering.Backoff);

        var url = stub.Url;
        stub.Dispose();
        using var dead = new CubicleClient(url, "t");
        var steering2 = new CubicleSteering(dead);
        Assert.Empty(await steering2.GetSteeringMessages());
        Assert.Equal(TimeSpan.FromSeconds(30), steering2.Backoff);
    }
}
