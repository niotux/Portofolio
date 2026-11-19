using System.Net;
using System.Text.Json.Nodes;
using PiSharp.Llm;
using PiSharp.Core.Config;

namespace PiSharp.Tests.Llm;

public class NotebookEmbeddingsClientTests
{
    [Fact]
    public async Task BatchIsOrderedByIndexAndUsesConfiguredEndpointAndModel()
    {
        var handler = new Handler();
        using var client = new NotebookEmbeddingsClient("http://localhost:8888/v1", "small-embedding", "test-token", handler);
        var vectors = await client.EmbedAsync(["first", "second"], default);
        Assert.Equal(new float[] { 1, 0 }, vectors[0]); Assert.Equal(new float[] { 0, 1 }, vectors[1]);
        Assert.Equal("http://localhost:8888/v1/embeddings", handler.Url);
        Assert.Equal("small-embedding", handler.Body!["model"]!.GetValue<string>());
        Assert.Equal(2, handler.Body["input"]!.AsArray().Count);
        Assert.Equal("Bearer test-token", handler.Authorization);
    }

    [Fact]
    public void EmbeddingSettingsParseAndProjectOverridesGlobal()
    {
        var lower = SettingsLoader.Parse("""{"notebookEmbeddingUrl":"http://local/v1","notebookEmbeddingModel":"first","notebookEmbeddingApiKeyEnv":"EMBED_KEY"}""");
        var higher = SettingsLoader.Parse("""{"notebookEmbeddingModel":"second"}""");
        var merged = SettingsLoader.Merge(lower, higher);
        Assert.Equal("second", merged.NotebookEmbeddingModel);
        Assert.Equal("http://local/v1", merged.NotebookEmbeddingUrl);
        Assert.Equal("EMBED_KEY", merged.NotebookEmbeddingApiKeyEnv);
    }

    [Theory]
    [InlineData("embeddinggemma", "title: none | text: saved note", "task: search result | query: task question")]
    [InlineData("other-model", "saved note", "task question")]
    public async Task RetrievalPrefixesAreModelSpecificAndIncludedInCacheIdentity(string model, string document, string query)
    {
        var handler = new Handler();
        using var client = new NotebookEmbeddingsClient("http://localhost/v1", model, handler: handler);
        await client.EmbedRetrievalAsync(["saved note"], "task question", default);
        Assert.Equal(document, handler.Body!["input"]![0]!.GetValue<string>());
        Assert.Equal(query, handler.Body["input"]![1]!.GetValue<string>());
        Assert.Equal(model == "embeddinggemma", client.Identity.EndsWith("#retrieval-prefix-v1"));
    }

    [Theory]
    [InlineData("{\"data\":[null]}")]
    [InlineData("{\"data\":[{\"embedding\":[1]}]}")]
    [InlineData("{\"data\":[{\"index\":0,\"embedding\":[null]}]}")]
    [InlineData("{\"data\":[{\"index\":0,\"embedding\":null}]}")]
    public async Task MalformedVectorsAreRejectedAsRecoverableProviderErrors(string response)
    {
        using var client = new NotebookEmbeddingsClient("http://localhost/v1", "embedding", handler: new Handler(response));
        await Assert.ThrowsAsync<ArgumentException>(() => client.EmbedAsync(["query"], default));
    }

    private sealed class Handler(string response = """{"data":[{"index":1,"embedding":[0,1]},{"index":0,"embedding":[1,0]}]}""") : HttpMessageHandler
    {
        public string? Url, Authorization;
        public JsonObject? Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Url = request.RequestUri!.AbsoluteUri; Authorization = request.Headers.Authorization?.ToString();
            Body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response) };
        }
    }
}
