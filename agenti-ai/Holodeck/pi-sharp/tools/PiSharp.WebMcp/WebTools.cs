using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

public static partial class WebTools
{
    private const int MaxPageBytes = 50 * 1024 * 1024;
    private static readonly Uri SearchEndpoint = new("https://www.bing.com/search");

    public static async Task<IResult> SearchAsync(JsonElement input, HttpClient client, CancellationToken cancellationToken)
    {
        var query = String(input, "query");
        if (string.IsNullOrWhiteSpace(query)) return Error("invalid_input", "query is required.");
        var maxResults = Integer(input, "max_results", 10);
        if (maxResults is < 1 or > 10) return Error("invalid_input", "max_results must be between 1 and 10.");

        try
        {
            var uri = new UriBuilder(SearchEndpoint) { Query = $"q={Uri.EscapeDataString(query)}&count={maxResults}" }.Uri;
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (compatible; PiSharpWebMcp/1.0)");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode) return Error("search_failed", $"Search backend returned HTTP {(int)response.StatusCode}.");
            var html = await ReadBodyAsync(response, MaxPageBytes, cancellationToken);
            var results = ParseSearchResults(html, maxResults);
            return Results.Json(new { results });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Error("timeout", "Search backend timed out.");
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException)
        {
            return Error("search_failed", "Search backend could not be reached.");
        }
    }

    public static async Task<IResult> FetchAsync(JsonElement input, HttpClient client, CancellationToken cancellationToken)
    {
        var rawUrl = String(input, "url");
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return Error("invalid_input", "url must be an absolute HTTP or HTTPS URL.");
        var maxChars = Integer(input, "max_chars", 10000);
        if (maxChars is < 1 or > 100000) return Error("invalid_input", "max_chars must be between 1 and 100000.");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (compatible; PiSharpWebMcp/1.0)");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode) return Error("fetch_failed", $"Page returned HTTP {(int)response.StatusCode}.");
            if (response.Content.Headers.ContentLength > MaxPageBytes)
                return Error("page_too_large", $"Page exceeds the {MaxPageBytes}-byte processing limit.");
            var html = await ReadBodyAsync(response, MaxPageBytes, cancellationToken);
            var markdown = HtmlToMarkdown(html);
            var truncated = markdown.Length > maxChars;
            if (truncated) markdown = markdown[..maxChars];
            return Results.Json(new { url = response.RequestMessage?.RequestUri?.ToString() ?? uri.ToString(), markdown, truncated });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Error("timeout", "Page fetch timed out.");
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException)
        {
            return Error("fetch_failed", "Page could not be fetched.");
        }
    }

    public static IReadOnlyList<SearchResult> ParseSearchResults(string html, int maxResults)
    {
        var results = new List<SearchResult>();
        foreach (Match match in BingResultRegex().Matches(html))
        {
            var anchor = BingTitleRegex().Match(match.Groups[1].Value);
            if (!anchor.Success) continue;
            var href = UnwrapBingUrl(WebUtility.HtmlDecode(anchor.Groups[1].Value));
            if (!Uri.TryCreate(href, UriKind.Absolute, out var target) || target.Scheme is not ("http" or "https")) continue;
            var title = CleanText(TagRegex().Replace(anchor.Groups[2].Value, " "));
            if (title.Length == 0) continue;
            var snippetMatch = BingSnippetRegex().Match(match.Groups[1].Value);
            var snippet = snippetMatch.Success
                ? CleanText(TagRegex().Replace(ScriptStyleRegex().Replace(snippetMatch.Groups[1].Value, " "), " "))
                : "";
            if (snippet.Length > 300) snippet = snippet[..300];
            results.Add(new SearchResult(title, target.ToString(), snippet));
            if (results.Count == maxResults) break;
        }
        return results;
    }

    public static string HtmlToMarkdown(string html)
    {
        html = ScriptStyleRegex().Replace(html, " ");
        html = CommentRegex().Replace(html, " ");
        html = HeadingRegex().Replace(html, match => $"\n\n{new string('#', match.Groups[1].Value.Length)} {match.Groups[2].Value}\n\n");
        html = LinkRegex().Replace(html, match => $"[{match.Groups[2].Value}]({match.Groups[1].Value})");
        html = ListTagRegex().Replace(html, "\n- ");
        html = BlockTagRegex().Replace(html, "\n\n");
        html = TagRegex().Replace(html, " ");
        html = WebUtility.HtmlDecode(html).Replace('\u00a0', ' ');
        var lines = html.Split('\n').Select(line => Regex.Replace(line, @"[\t\r ]+", " ").Trim());
        var text = Regex.Replace(string.Join("\n", lines), @"\n{3,}", "\n\n").Trim();
        return text;
    }

    private static async Task<string> ReadBodyAsync(HttpResponseMessage response, int maxBytes, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (output.Length + read > maxBytes) throw new InvalidDataException("Response exceeds processing limit.");
            output.Write(buffer, 0, read);
        }
        return Encoding.UTF8.GetString(output.GetBuffer(), 0, (int)output.Length);
    }

    private static string? String(JsonElement input, string name) =>
        input.ValueKind == JsonValueKind.Object && input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int Integer(JsonElement input, string name, int fallback) =>
        input.ValueKind == JsonValueKind.Object && input.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : fallback;

    private static string UnwrapBingUrl(string href)
    {
        if (!Uri.TryCreate(href, UriKind.Absolute, out var uri) || !uri.Host.EndsWith("bing.com", StringComparison.OrdinalIgnoreCase)) return href;
        var encoded = uri.Query.TrimStart('?').Split('&').Select(part => part.Split('=', 2))
            .FirstOrDefault(pair => pair.Length == 2 && pair[0] == "u")?[1];
        if (encoded is null || !encoded.StartsWith("a1", StringComparison.Ordinal)) return href;
        try
        {
            var value = encoded[2..].Replace('-', '+').Replace('_', '/');
            value = value.PadRight((value.Length + 3) / 4 * 4, '=');
            return Encoding.UTF8.GetString(Convert.FromBase64String(value));
        }
        catch (FormatException) { return href; }
    }

    private static string CleanText(string text) => Regex.Replace(WebUtility.HtmlDecode(text), @"[\s\u00a0]+", " ").Trim();

    private static IResult Error(string code, string message) => Results.Json(new { error = new { code, message } });

    [GeneratedRegex("<li\\b[^>]*class=[\\\"'][^\\\"']*\\bb_algo\\b[^\\\"']*[\\\"'][^>]*>(.*?)</li\\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex BingResultRegex();
    [GeneratedRegex("<h2\\b[^>]*>\\s*<a\\b[^>]*href=[\\\"']([^\\\"']+)[\\\"'][^>]*>(.*?)</a\\s*>\\s*</h2>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex BingTitleRegex();
    [GeneratedRegex("<p\\b[^>]*>(.*?)</p\\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex BingSnippetRegex();
    [GeneratedRegex("<script\\b[^>]*>.*?</script\\s*>|<style\\b[^>]*>.*?</style\\s*>|<noscript\\b[^>]*>.*?</noscript\\s*>|<svg\\b[^>]*>.*?</svg\\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptStyleRegex();
    [GeneratedRegex("<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex CommentRegex();
    [GeneratedRegex("</?(?:p|div|article|section|header|footer|main|h[1-6]|blockquote|pre|br|hr|tr|table|li|ul|ol)\\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockTagRegex();
    [GeneratedRegex("</?(?:li|dt|dd)\\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ListTagRegex();
    [GeneratedRegex("<h([1-6])\\b[^>]*>(.*?)</h[1-6]\\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex HeadingRegex();
    [GeneratedRegex("<a\\b[^>]*href=[\\\"']([^\\\"']+)[\\\"'][^>]*>(.*?)</a\\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex LinkRegex();
    [GeneratedRegex("<[^>]+>", RegexOptions.Singleline)]
    private static partial Regex TagRegex();

    public sealed record SearchResult(string Title, string Url, string Snippet);
}
