using System.IO.Pipelines;
using System.Text.Json.Nodes;
using PiSharp.Core.Mcp;

namespace PiSharp.Tests.Mcp;

public class JsonRpcTests
{
    [Fact]
    public async Task RequestReturnsResult()
    {
        await using var pipes = new RpcPipes();
        await using var server = new JsonRpcPeer(pipes.ServerIn, pipes.ServerOut, (method, p, _) =>
        {
            Assert.Equal("add", method);
            var a = p!["a"]!.GetValue<int>();
            var b = p["b"]!.GetValue<int>();
            return Task.FromResult<JsonNode?>(JsonValue.Create(a + b));
        });
        await using var client = new JsonRpcPeer(pipes.ClientIn, pipes.ClientOut);

        var result = await client.RequestAsync("add", new JsonObject { ["a"] = 2, ["b"] = 3 });
        Assert.Equal(5, result!.GetValue<int>());
    }

    [Fact]
    public async Task MethodNotFoundIsJsonRpcError()
    {
        await using var pipes = new RpcPipes();
        await using var server = new JsonRpcPeer(pipes.ServerIn, pipes.ServerOut); // no handler
        await using var client = new JsonRpcPeer(pipes.ClientIn, pipes.ClientOut);

        var ex = await Assert.ThrowsAsync<JsonRpcException>(() => client.RequestAsync("nope", null));
        Assert.Equal(JsonRpcException.MethodNotFound, ex.Code);
    }

    [Fact]
    public async Task NotificationIsDelivered()
    {
        await using var pipes = new RpcPipes();
        var got = new TaskCompletionSource<(string, JsonNode?)>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new JsonRpcPeer(pipes.ServerIn, pipes.ServerOut);
        server.Notification += (m, p) => got.TrySetResult((m, p));
        await using var client = new JsonRpcPeer(pipes.ClientIn, pipes.ClientOut);

        await client.NotifyAsync("boom", new JsonObject { ["x"] = 1 });
        var (method, p) = await got.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("boom", method);
        Assert.Equal(1, p!["x"]!.GetValue<int>());
    }

    [Fact]
    public async Task HandlerExceptionBecomesInternalError()
    {
        await using var pipes = new RpcPipes();
        await using var server = new JsonRpcPeer(pipes.ServerIn, pipes.ServerOut,
            (_, _, _) => throw new InvalidOperationException("nope"));
        await using var client = new JsonRpcPeer(pipes.ClientIn, pipes.ClientOut);

        var ex = await Assert.ThrowsAsync<JsonRpcException>(() => client.RequestAsync("x", null));
        Assert.Equal(JsonRpcException.InternalError, ex.Code);
        Assert.Equal("nope", ex.Message);
    }
}

internal sealed class RpcPipes : IAsyncDisposable
{
    private readonly Pipe _c2s = new();
    private readonly Pipe _s2c = new();

    public Stream ClientIn => _s2c.Reader.AsStream();
    public Stream ClientOut => _c2s.Writer.AsStream();
    public Stream ServerIn => _c2s.Reader.AsStream();
    public Stream ServerOut => _s2c.Writer.AsStream();

    public async ValueTask DisposeAsync()
    {
        await _c2s.Writer.CompleteAsync();
        await _s2c.Writer.CompleteAsync();
    }
}
