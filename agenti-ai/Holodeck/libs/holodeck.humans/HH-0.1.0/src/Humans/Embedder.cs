using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Humans;

// Adapted from Person.Core/Embedder: query instruction only on queries, leading configured dimensions, cosine search.
public sealed class Embedder(HttpClient http, EmbeddingOptions options, string queryInstruction)
{
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
  public string Tag => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(options.BaseUrl + "\n" + options.Name + "\n" + options.Dimensions)));

  public async Task<float[]> EmbedAsync(string text, bool query, CancellationToken token)
  {
    string input = query ? "Instruct: " + queryInstruction + "\nQuery: " + text : text;
    using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
    timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
    Uri address = new(new Uri(options.BaseUrl.TrimEnd('/') + "/"), "embeddings");
    using HttpResponseMessage response = await http.PostAsJsonAsync(address, new EmbeddingBody(options.Name, input), Json, timeout.Token);
    if (!response.IsSuccessStatusCode)
    {
      throw new HttpRequestException($"Embedding service returned HTTP {(int)response.StatusCode}. Check the Embedding settings.");
    }

    EmbeddingResponse result = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(Json, timeout.Token) ?? throw new IOException("The embedding service returned no vector.");
    if (result.Data is null)
    {
      throw new IOException("The embedding service returned no vector data.");
    }
    if (result.Data.Count == 0 || result.Data[0].Embedding is null || result.Data[0].Embedding.Length < options.Dimensions)
    {
      throw new IOException("The embedding vector does not match the configured dimensions.");
    }

    float[] vector = new float[options.Dimensions];
    double norm = 0;
    for (int i = 0; i < vector.Length; i++)
    {
      float value = result.Data[0].Embedding[i];
      if (!float.IsFinite(value))
      {
        throw new IOException("The embedding vector contains a non-finite value.");
      }
      vector[i] = value;
      norm += value * (double)value;
    }
    if (norm == 0)
    {
      throw new IOException("The embedding service returned a zero vector.");
    }
    return vector;
  }
}
