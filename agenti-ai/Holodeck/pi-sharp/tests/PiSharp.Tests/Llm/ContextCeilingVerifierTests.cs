// ContextCeilingVerifier against an in-memory HttpMessageHandler: no sockets, no real server.
using System.Net;
using System.Text;
using PiSharp.Core.Models;
using PiSharp.Llm;

namespace PiSharp.Tests.Llm;

internal sealed class CeilingFakeHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _responder;

    public CeilingFakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) => _responder = responder;

    public List<Uri> Uris { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Uris.Add(request.RequestUri!);
        return _responder(request);
    }
}

internal static class CeilingFakes
{
    public static CeilingFakeHandler Json(string path, string body) => new(req =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        }));

    public static CeilingFakeHandler ByPath(Func<string, HttpResponseMessage> responder) => new(req =>
        Task.FromResult(responder(req.RequestUri!.AbsolutePath)));
}

public class ContextCeilingVerifierTests
{
    [Fact]
    public async Task CurrentLlamaPropsAlreadyReportsPerSlotLimitAndUsesConfiguredAuthentication()
    {
        var handler = new CeilingFakeHandler(req =>
        {
            Assert.Equal("Bearer", req.Headers.Authorization?.Scheme);
            Assert.Equal("test-key", req.Headers.Authorization?.Parameter);
            return Task.FromResult(Json(HttpStatusCode.OK, "{\"default_generation_settings\":{\"n_ctx\":200192},\"total_slots\":4}"));
        });
        var verdict = await ContextCeilingVerifier.VerifyAsync(Model("maxiclaude", 100000) with { ApiKey = "test-key" }, 8192, handler);
        Assert.True(verdict.Verified);
        Assert.Equal(200192, verdict.ProviderLimit);
        Assert.Single(handler.Uris);
    }

    private static ModelConfig Model(string id, int contextWindow, string baseUrl = "http://127.0.0.1:8080/v1") =>
        new() { Id = id, BaseUrl = baseUrl, ContextWindow = contextWindow };

    // ---- pure Evaluate ---------------------------------------------------------

    [Fact]
    public void Evaluate_TriggerBelowLimit_OkAndVerified()
    {
        var v = ContextCeilingVerifier.Evaluate(65536, 16384, 50048);
        Assert.True(v.Ok);
        Assert.False(v.TriggerUnreachable);
        Assert.Equal(49152, v.TriggerTokens); // 65536 - 16384
        Assert.Equal(50048, v.ProviderLimit);
        Assert.True(v.Verified);
        Assert.Empty(v.Warnings);
    }

    [Fact]
    public void Evaluate_TriggerEqualsLimit_TriggerUnreachable()
    {
        var v = ContextCeilingVerifier.Evaluate(65536, 16384, 49152);
        Assert.False(v.Ok);
        Assert.True(v.TriggerUnreachable);
        Assert.NotNull(v.Error);
        Assert.Contains("49152", v.Error);
    }

    [Fact]
    public void Evaluate_TriggerAboveLimit_TriggerUnreachable()
    {
        var v = ContextCeilingVerifier.Evaluate(131072, 16384, 50048);
        Assert.False(v.Ok);
        Assert.True(v.TriggerUnreachable);
        Assert.False(v.Verified);
    }

    [Fact]
    public void Evaluate_NoLimit_OkWithWarning()
    {
        var v = ContextCeilingVerifier.Evaluate(65536, 16384, null);
        Assert.True(v.Ok);
        Assert.False(v.Verified);
        Assert.Single(v.Warnings);
    }

    // ---- llama.cpp /props ------------------------------------------------------

    [Fact]
    public async Task LlamaCpp_PerAgentIsNctxOverSlots()
    {
        // n_ctx 200192 across 4 slots -> per-request 50048.
        var handler = CeilingFakes.ByPath(p => p == "/props"
            ? Json(HttpStatusCode.OK, "{\"n_ctx\":\"200192\",\"total_slots\":\"4\"}")
            : Json(HttpStatusCode.NotFound, "{}"));
        var v = await ContextCeilingVerifier.VerifyAsync(Model("maxiclaude", 65536), 16384, handler);
        Assert.True(v.Ok);
        Assert.Equal(50048, v.ProviderLimit);
        Assert.True(v.Verified);
        Assert.Empty(v.Warnings);
    }

    [Fact]
    public async Task LlamaCpp_MissingTotalSlots_TreatedAsOneWithWarning()
    {
        var handler = CeilingFakes.ByPath(p => p == "/props"
            ? Json(HttpStatusCode.OK, "{\"n_ctx\":\"65536\"}")
            : Json(HttpStatusCode.NotFound, "{}"));
        var v = await ContextCeilingVerifier.VerifyAsync(Model("maxiclaude", 65536), 16384, handler);
        // n_ctx 65536 / 1 slot = 65536 >= trigger (49152) is fine; but the slot warning must appear.
        Assert.Contains(v.Warnings, w => w.Contains("total_slots"));
        Assert.Equal(65536, v.ProviderLimit);
    }

    [Fact]
    public async Task LlamaCpp_NctxAsNumber_NotString_StillParses()
    {
        var handler = CeilingFakes.ByPath(p => p == "/props"
            ? Json(HttpStatusCode.OK, "{\"n_ctx\":200192,\"total_slots\":4}")
            : Json(HttpStatusCode.NotFound, "{}"));
        var v = await ContextCeilingVerifier.VerifyAsync(Model("maxiclaude", 65536), 16384, handler);
        Assert.Equal(50048, v.ProviderLimit);
        Assert.True(v.Verified);
    }

    // ---- vLLM /v1/models -------------------------------------------------------

    [Fact]
    public async Task Vllm_UsesMaxModelLen()
    {
        var handler = CeilingFakes.ByPath(p => p == "/v1/models"
            ? Json(HttpStatusCode.OK, "[{\"id\":\"maxiclaude\",\"max_model_len\":200000}]")
            : Json(HttpStatusCode.NotFound, "{}"));
        var v = await ContextCeilingVerifier.VerifyAsync(Model("maxiclaude", 65536), 16384, handler);
        Assert.True(v.Ok);
        Assert.Equal(200000, v.ProviderLimit);
        Assert.True(v.Verified);
    }

    [Fact]
    public async Task Vllm_ModelIdMissing_FailsFast()
    {
        var handler = CeilingFakes.ByPath(p => p == "/v1/models"
            ? Json(HttpStatusCode.OK, "[{\"id\":\"other\",\"max_model_len\":200000}]")
            : Json(HttpStatusCode.NotFound, "{}"));
        var v = await ContextCeilingVerifier.VerifyAsync(Model("maxiclaude", 65536), 16384, handler);
        Assert.False(v.Ok);
        Assert.NotNull(v.Error);
        Assert.Contains("not reported", v.Error);
    }

    [Fact]
    public async Task Vllm_HasIdNoMaxModelLen_OkWithUnverifiedWarning()
    {
        var handler = CeilingFakes.ByPath(p => p == "/v1/models"
            ? Json(HttpStatusCode.OK, "[{\"id\":\"maxiclaude\"}]")
            : Json(HttpStatusCode.NotFound, "{}"));
        var v = await ContextCeilingVerifier.VerifyAsync(Model("maxiclaude", 65536), 16384, handler);
        Assert.True(v.Ok);
        Assert.False(v.Verified);
        Assert.Contains(v.Warnings, w => w.Contains("unverified"));
    }

    // ---- unreachability --------------------------------------------------------

    [Fact]
    public async Task UnreachableProvider_WarnsNotCrashes()
    {
        var handler = new CeilingFakeHandler(_ =>
            throw new System.Net.Http.HttpRequestException("connection refused"));
        var v = await ContextCeilingVerifier.VerifyAsync(Model("maxiclaude", 65536), 16384, handler);
        Assert.True(v.Ok);
        Assert.False(v.Verified);
        Assert.Contains(v.Warnings, w => w.Contains("unverified"));
    }

    [Fact]
    public void HostOf_StripsTrailingV1()
    {
        Assert.Equal("http://127.0.0.1:8080", ContextCeilingVerifier.HostOf("http://127.0.0.1:8080/v1"));
        Assert.Equal("http://127.0.0.1:8080", ContextCeilingVerifier.HostOf("http://127.0.0.1:8080/v1/"));
        Assert.Equal("http://127.0.0.1:8080", ContextCeilingVerifier.HostOf("http://127.0.0.1:8080"));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
}
