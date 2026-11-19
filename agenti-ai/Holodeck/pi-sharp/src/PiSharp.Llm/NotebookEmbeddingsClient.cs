using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using PiSharp.Core.Notebook;

namespace PiSharp.Llm;

/// <summary>Explicitly configured OpenAI-compatible embedding endpoint; never uses the generation model or paid fallback.</summary>
public sealed class NotebookEmbeddingsClient : INotebookEmbeddings, IDisposable
{
    private readonly HttpClient _http;
    private readonly Uri _endpoint;
    private readonly string _model;
    private bool IsEmbeddingGemma => string.Equals(_model, "embeddinggemma", StringComparison.OrdinalIgnoreCase);
    public string Identity => _endpoint.AbsoluteUri + "#" + _model + (IsEmbeddingGemma ? "#retrieval-prefix-v1" : "");
    public NotebookEmbeddingsClient(string baseUrl, string model, string? apiKey = null, HttpMessageHandler? handler = null)
    {
        _endpoint = new Uri(baseUrl.TrimEnd('/') + "/embeddings");
        if (_endpoint.Scheme is not ("http" or "https")) throw new ArgumentException("Embedding URL must be HTTP(S).");
        _model = model;
        _http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { MaxResponseContentBufferSize = 4 * 1024 * 1024 };
        if (!string.IsNullOrWhiteSpace(apiKey)) _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }
    public Task<IReadOnlyList<float[]>> EmbedRetrievalAsync(IReadOnlyList<string> documents, string? query, CancellationToken ct)
    {
        if (!IsEmbeddingGemma) return EmbedAsync(query is null ? documents : [.. documents, query], ct);
        // Match ai-factory's EmbeddingClient: asymmetric retrieval prompts, with a versioned cache identity.
        var inputs = documents.Select(text => "title: none | text: " + text).ToList();
        if (query is not null) inputs.Add("task: search result | query: " + query);
        return EmbedAsync(inputs, ct);
    }

    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct)
    {
        var body = new JsonObject { ["model"] = _model, ["input"] = new JsonArray(texts.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray()) };
        using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(_endpoint, content, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var root = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false)) as JsonObject ?? throw new ArgumentException("Invalid embedding response.");
        var rows = root["data"] as JsonArray ?? throw new ArgumentException("Invalid embedding response data.");
        var ordered = rows.Select(n =>
        {
            var row = n as JsonObject ?? throw new ArgumentException("Invalid embedding response row.");
            if (row["index"] is not JsonValue value || !value.TryGetValue<int>(out _))
                throw new ArgumentException("Invalid embedding response index.");
            return row;
        }).OrderBy(n => n["index"]!.GetValue<int>()).ToArray();
        if (!ordered.Select(n => n["index"]!.GetValue<int>()).SequenceEqual(Enumerable.Range(0, texts.Count))) throw new ArgumentException("Invalid embedding response indexes.");
        return ordered.Select(n => (n["embedding"] as JsonArray ?? throw new ArgumentException("Invalid embedding response vector."))
            .Select(v => v is JsonValue value && value.TryGetValue<float>(out var number)
                ? number : throw new ArgumentException("Invalid embedding response component.")).ToArray()).ToArray();
    }
    public void Dispose() => _http.Dispose();
}
