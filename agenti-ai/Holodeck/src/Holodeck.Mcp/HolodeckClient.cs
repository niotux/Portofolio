using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Unicode;
using ModelContextProtocol;

namespace Holodeck.Mcp;

// The app API as the browser uses it. Responses go back to the model as the API's own JSON; a refused request becomes
// a tool error carrying the API's status and body, so its field errors and conflicts reach the caller unchanged.
public sealed class HolodeckClient
{
  public static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(10);
  public const int MaxInlineTextBytes = 256 * 1024;
  private const string DoneText = "Done.";
  private const string DownloadFolder = "holodeck-mcp";
  private const string FallbackFileName = "file";
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
  private readonly HttpClient http;

  public HolodeckClient(HttpClient http)
  {
    this.http = http;
  }

  public static string Segment(string value)
  {
    return Uri.EscapeDataString(value);
  }

  public async Task<string> GetAsync(string path, CancellationToken token)
  {
    using HttpResponseMessage response = await http.GetAsync(path, token);
    return await ReadAsync(response, token);
  }

  public async Task<T> GetJsonAsync<T>(string path, CancellationToken token)
  {
    using HttpResponseMessage response = await http.GetAsync(path, token);
    await EnsureSuccessAsync(response, token);
    T? value = await response.Content.ReadFromJsonAsync<T>(Json, token);
    if (value is null)
    {
      throw new McpException($"Holodeck returned no content for {path}.");
    }

    return value;
  }

  public async Task<string> SendAsync(HttpMethod method, string path, CancellationToken token)
  {
    using HttpRequestMessage request = new(method, path);
    using HttpResponseMessage response = await http.SendAsync(request, token);
    return await ReadAsync(response, token);
  }

  public async Task<string> SendJsonAsync<T>(HttpMethod method, string path, T body, CancellationToken token)
  {
    using HttpRequestMessage request = new(method, path);
    request.Content = JsonContent.Create(body, options: Json);
    using HttpResponseMessage response = await http.SendAsync(request, token);
    return await ReadAsync(response, token);
  }

  // Sends a note as You, with local files attached the way the composer uploads them.
  public async Task<string> SendFormAsync(string path, string note, List<string> files, CancellationToken token)
  {
    using MultipartFormDataContent form = new();
    form.Add(new StringContent(note), "note");
    foreach (string file in files)
    {
      if (!File.Exists(file))
      {
        throw new McpException($"There is no file at {file}.");
      }

      form.Add(new StreamContent(File.OpenRead(file)), "files", Path.GetFileName(file));
    }

    using HttpResponseMessage response = await http.PostAsync(path, form, token);
    return await ReadAsync(response, token);
  }

  // Small UTF-8 text comes back inline. Anything else is saved to a local temporary file, and the answer says where.
  public async Task<string> DownloadAsync(string path, string name, CancellationToken token)
  {
    using HttpResponseMessage response = await http.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, token);
    await EnsureSuccessAsync(response, token);
    string mediaType = response.Content.Headers.ContentType?.MediaType ?? "unknown";
    long? length = response.Content.Headers.ContentLength;
    bool isLarge = length is not null && length.Value > MaxInlineTextBytes;
    if (!isLarge)
    {
      byte[] content = await response.Content.ReadAsByteArrayAsync(token);
      if (IsInlineText(content))
      {
        return Encoding.UTF8.GetString(content);
      }

      string saved = DownloadTarget(name);
      await File.WriteAllBytesAsync(saved, content, token);
      return SavedText(saved, content.Length, mediaType);
    }

    string target = DownloadTarget(name);
    await using (FileStream file = File.Create(target))
    {
      await response.Content.CopyToAsync(file, token);
    }

    return SavedText(target, new FileInfo(target).Length, mediaType);
  }

  public static bool IsInlineText(ReadOnlySpan<byte> content)
  {
    return content.Length <= MaxInlineTextBytes && content.IndexOf((byte)0) < 0 && Utf8.IsValid(content);
  }

  private static string DownloadTarget(string name)
  {
    ReadOnlySpan<char> fileName = Path.GetFileName(name.AsSpan());
    string folder = Path.Combine(Path.GetTempPath(), DownloadFolder, Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder);
    return Path.Combine(folder, fileName.IsEmpty ? FallbackFileName : fileName.ToString());
  }

  private static string SavedText(string path, long length, string mediaType)
  {
    return $"Saved {length} bytes ({mediaType}) to {path}";
  }

  private static async Task<string> ReadAsync(HttpResponseMessage response, CancellationToken token)
  {
    await EnsureSuccessAsync(response, token);
    if (response.StatusCode == HttpStatusCode.NoContent)
    {
      return DoneText;
    }

    return await response.Content.ReadAsStringAsync(token);
  }

  private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken token)
  {
    if (response.IsSuccessStatusCode)
    {
      return;
    }

    string body = await response.Content.ReadAsStringAsync(token);
    throw new McpException($"Holodeck refused the request ({(int)response.StatusCode} {response.ReasonPhrase}): {body}");
  }
}
