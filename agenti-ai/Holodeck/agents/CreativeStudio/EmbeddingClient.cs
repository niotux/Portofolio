using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CreativeStudio;

public sealed class EmbeddingClient(HttpClient http, EmbeddingOptions options)
{
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
  public string Tag => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(options.BaseUrl + "\n" + options.Name + "\n" + options.Dimensions)));

  // Only formed prose notes or a deliberate descriptive query belong here; callers own that distinction.
  public async Task<float[]> EmbedAsync(string prose, CancellationToken token)
  {
    using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
    timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
    Uri address = new(new Uri(options.BaseUrl.TrimEnd('/') + "/"), "embeddings");
    using HttpRequestMessage request = new(HttpMethod.Post, address);
    request.Content = JsonContent.Create(new EmbeddingRequest(options.Name, prose), options: Json);
    if (!string.IsNullOrEmpty(options.ApiKey))
    {
      request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
    }
    using HttpResponseMessage response = await http.SendAsync(request, timeout.Token);
    if (!response.IsSuccessStatusCode)
    {
      throw new HttpRequestException($"The embedding service returned HTTP {(int)response.StatusCode}.");
    }

    EmbeddingResponse result;
    try
    {
      result = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(Json, timeout.Token) ?? throw new IOException("The embedding service returned no vector.");
    }
    catch (JsonException exception)
    {
      throw new IOException("The embedding service returned an invalid response.", exception);
    }
    if (result.Data is null)
    {
      throw new IOException("The embedding service returned no vector data.");
    }
    if (result.Data.Count == 0)
    {
      throw new IOException("The embedding service returned no vector data.");
    }
    return ReadVector(result.Data[0].Embedding);
  }

  private float[] ReadVector(float[]? source)
  {
    if (source is null)
    {
      throw new IOException("The embedding service returned no vector.");
    }
    if (source.Length < options.Dimensions || options.Dimensions < 1)
    {
      throw new IOException("The embedding vector does not match the configured dimensions.");
    }
    float[] vector = new float[options.Dimensions];
    double norm = 0;
    for (int index = 0; index < vector.Length; index++)
    {
      float value = source[index];
      if (!float.IsFinite(value))
      {
        throw new IOException("The embedding vector contains a non-finite value.");
      }
      vector[index] = value;
      norm += value * (double)value;
    }
    if (norm == 0)
    {
      throw new IOException("The embedding service returned a zero vector.");
    }
    return vector;
  }
}
