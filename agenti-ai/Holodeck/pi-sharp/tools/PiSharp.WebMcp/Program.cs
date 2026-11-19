using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

var builder = WebApplication.CreateBuilder(args);
var identityUrl = builder.Configuration["KB_IDENTITY_URL"] ?? "http://127.0.0.1:8081/whoami";
var timeoutSeconds = int.TryParse(builder.Configuration["OUTBOUND_TIMEOUT_SECONDS"], out var configuredTimeout)
    ? Math.Clamp(configuredTimeout, 1, 300) : 30;
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient("identity", client => client.Timeout = TimeSpan.FromSeconds(timeoutSeconds));
builder.Services.AddHttpClient("outbound", client => client.Timeout = TimeSpan.FromSeconds(timeoutSeconds));
builder.Services.AddSingleton(new IdentityOptions(new Uri(identityUrl, UriKind.Absolute)));

var app = builder.Build();
app.Use(async (context, next) =>
{
    var authorization = context.Request.Headers.Authorization.ToString();
    if (!AuthenticationHeaderValue.TryParse(authorization, out var header) ||
        !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) ||
        string.IsNullOrWhiteSpace(header.Parameter))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }

    var cache = context.RequestServices.GetRequiredService<IMemoryCache>();
    var token = header.Parameter;
    var key = "identity:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
    if (!cache.TryGetValue(key, out IdentityResult? identity))
    {
        identity = await ResolveIdentityAsync(context, token);
        cache.Set(key, identity, identity.Valid ? TimeSpan.FromSeconds(60) : TimeSpan.FromSeconds(10));
    }

    if (identity is null || !identity.Valid)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }
    context.Items["seat"] = identity.Seat;
    await next();
});

app.MapGet("/health", (HttpContext context) => Results.Ok(new { status = "ok", service = "pi-sharp-web-mcp", seat = context.Items["seat"] }));
app.MapGet("/whoami", (HttpContext context) => Results.Ok(context.Items["seat"]));
app.MapGet("/mcp/tools", () => Results.Ok(new
{
    tools = new object[]
    {
        new { name = "web_search", description = "Search the web for relevant pages.", inputSchema = new { type = "object", properties = new { query = new { type = "string" }, max_results = new { type = "integer", minimum = 1, maximum = 10, @default = 10 } }, required = new[] { "query" } } },
        new { name = "web_fetch", description = "Fetch a web page and convert it to markdown.", inputSchema = new { type = "object", properties = new { url = new { type = "string", format = "uri" }, max_chars = new { type = "integer", minimum = 1, maximum = 100000, @default = 10000 } }, required = new[] { "url" } } }
    }
}));
app.MapPost("/mcp/tools/web_search", async (HttpContext context, JsonElement input) =>
    await WebTools.SearchAsync(input, context.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient("outbound"), context.RequestAborted));
app.MapPost("/mcp/tools/web_fetch", async (HttpContext context, JsonElement input) =>
    await WebTools.FetchAsync(input, context.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient("outbound"), context.RequestAborted));
app.MapPost("/mcp/tools/{tool}", (string tool) => Results.NotFound());
app.Run();

static async Task<IdentityResult> ResolveIdentityAsync(HttpContext context, string token)
{
    try
    {
        var options = context.RequestServices.GetRequiredService<IdentityOptions>();
        var client = context.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient("identity");
        using var request = new HttpRequestMessage(HttpMethod.Get, options.Url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, context.RequestAborted);
        if (!response.IsSuccessStatusCode) return IdentityResult.Invalid;
        await using var stream = await response.Content.ReadAsStreamAsync(context.RequestAborted);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: context.RequestAborted);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("slug", out var slug) || slug.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(slug.GetString()))
            return IdentityResult.Invalid;
        return new IdentityResult(true, document.RootElement.Clone());
    }
    catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or UriFormatException)
    {
        return IdentityResult.Invalid;
    }
}

internal sealed record IdentityOptions(Uri Url);
internal sealed record IdentityResult(bool Valid, JsonElement Seat)
{
    public static IdentityResult Invalid { get; } = new(false, default);
}

public partial class Program;
