// adr-visual-toolkits section 6: a capture the seat chooses to publish goes to the factory KB's shared capture store,
// where the owner sees it in the seat's cubicle (workspace Captures panel). The notebook artifact stays the seat's own.
using System.Net.Http.Headers;
using System.Text.Json;

namespace PiSharp.Tools.Desktop;

/// <summary>Publishes one capture to the factory and returns the id it was stored under.</summary>
public interface ICapturePublisher
{
    Task<long> PublishAsync(byte[] image, string? workItem, string? caption, IReadOnlyDictionary<string, string> meta, CancellationToken cancellationToken);
}

/// <summary>POST {kb}/captures with the seat's own token, the image as the raw body (KB 1.0.123 and later).</summary>
public sealed class HttpCapturePublisher : ICapturePublisher, IDisposable
{
    private readonly HttpClient _http;

    /// <param name="kbUrl">The factory's KB MCP address (…/mcp); captures go to the same authority.</param>
    public HttpCapturePublisher(string kbUrl, string token, HttpMessageHandler? handler = null)
    {
        var endpoint = new Uri(kbUrl);
        _http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false })
        { BaseAddress = new Uri(endpoint.GetLeftPart(UriPartial.Authority)), Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task<long> PublishAsync(byte[] image, string? workItem, string? caption, IReadOnlyDictionary<string, string> meta, CancellationToken cancellationToken)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(workItem)) query.Add("work_item=" + Uri.EscapeDataString(workItem));
        if (!string.IsNullOrWhiteSpace(caption)) query.Add("caption=" + Uri.EscapeDataString(caption));
        query.AddRange(meta.Select(p => $"meta.{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
        using var body = new ByteArrayContent(image);
        body.Headers.ContentType = new MediaTypeHeaderValue(image is [0xFF, 0xD8, 0xFF, ..] ? "image/jpeg" : "image/png");
        using var response = await _http.PostAsync("/captures" + (query.Count > 0 ? "?" + string.Join('&', query) : ""), body, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"the KB refused the capture ({(int)response.StatusCode}): {(text.Length > 300 ? text[..300] : text)}");
        using var json = JsonDocument.Parse(text);
        return json.RootElement.GetProperty("id").GetInt64();
    }

    public void Dispose() => _http.Dispose();
}
