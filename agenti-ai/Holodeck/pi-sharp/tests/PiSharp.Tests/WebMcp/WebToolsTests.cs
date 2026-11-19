using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace PiSharp.Tests.WebMcp;

public sealed class WebToolsTests
{
    [Fact]
    public void HtmlToMarkdownRemovesExecutableAndHiddenContentAndKeepsReadableBlocks()
    {
        var markdown = WebTools.HtmlToMarkdown("<html><head><style>.x{color:red}</style></head><body><h1>Welcome &amp; hello</h1><script>alert('x')</script><p>Readable <b>text</b>.</p><svg><text>hidden</text></svg></body></html>");

        Assert.Contains("Welcome & hello", markdown);
        Assert.Contains("Readable text .", markdown);
        Assert.DoesNotContain("<h1", markdown);
        Assert.DoesNotContain("alert", markdown);
        Assert.DoesNotContain("color:red", markdown);
        Assert.DoesNotContain("hidden", markdown);
    }

    [Fact]
    public async Task FetchConvertsHtmlAndTruncatesAtRequestedCharacterLimit()
    {
        using var input = JsonDocument.Parse("{\"url\":\"https://example.org/page\",\"max_chars\":6}");
        using var client = new HttpClient(new StaticResponseHandler("<html><body><h1>Hello</h1><script>bad()</script><p>World!</p></body></html>"));

        var result = await WebTools.FetchAsync(input.RootElement, client, CancellationToken.None);
        using var json = await ReadResultJsonAsync(result);

        Assert.Equal("# Hell", json.RootElement.GetProperty("markdown").GetString());
        Assert.True(json.RootElement.GetProperty("truncated").GetBoolean());
        Assert.DoesNotContain("<", json.RootElement.GetProperty("markdown").GetString());
    }

    [Fact]
    public async Task SearchTimeoutReturnsStructuredError()
    {
        using var input = JsonDocument.Parse("{\"query\":\"timeout probe\"}");
        using var client = new HttpClient(new DelayedResponseHandler()) { Timeout = TimeSpan.FromMilliseconds(30) };

        var result = await WebTools.SearchAsync(input.RootElement, client, CancellationToken.None);
        using var json = await ReadResultJsonAsync(result);

        Assert.Equal("timeout", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void SearchResultsAreParsedAndCappedWithTitleUrlAndSnippet()
    {
        const string html = "<li class=\"b_algo\"><h2><a href=\"https://www.bing.com/ck/a?u=a1aHR0cHM6Ly9leGFtcGxlLm9yZy9h&amp;ntb=1\">First &amp; result</a></h2><p>Short snippet.</p></li><li class=\"b_algo\"><h2><a href=\"https://example.net/b\">Second result</a></h2></li>";

        var results = WebTools.ParseSearchResults(html, 1);

        var result = Assert.Single(results);
        Assert.Equal("First & result", result.Title);
        Assert.Equal("https://example.org/a", result.Url);
        Assert.Contains("Short snippet.", result.Snippet);
    }

    private static async Task<JsonDocument> ReadResultJsonAsync(IResult result)
    {
        var context = new DefaultHttpContext();
        context.RequestServices = new Microsoft.Extensions.DependencyInjection.ServiceCollection()
            .AddLogging().AddOptions().BuildServiceProvider();
        context.Response.Body = new MemoryStream();
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        return await JsonDocument.ParseAsync(context.Response.Body);
    }

    private sealed class StaticResponseHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(body)
            });
    }

    private sealed class DelayedResponseHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        }
    }
}
