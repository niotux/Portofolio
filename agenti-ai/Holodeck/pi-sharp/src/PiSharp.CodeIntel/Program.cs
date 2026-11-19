// Minimal-ASP.NET host for the pi-sharp code-intel service.
//
//   GET  /health   -> 200 {status, seat, kb} when the bearer token is valid (seat identity in the body)
//   GET  /whoami   -> the KB's own /whoami, passed through (the KB is the identity provider)
//   POST /mcp      -> MCP streamable-HTTP (code_attach / code_sync / code_detach tools)
//   GET  /mcp      -> keep-alive ping for a live session
//   DELETE /mcp    -> end a session
//
// Bearer seat-token auth is applied to /mcp and /whoami (see Auth/CodeIntelAuth.cs). /health is
// public but reports the seat identity when one is present, so the deployment can probe both the
// service and a token in one request.
using System.Net;
using System.Reflection;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using PiSharp.CodeIntel;
using PiSharp.CodeIntel.Auth;
using PiSharp.CodeIntel.Mcp;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCodeIntelOptions(builder.Configuration);
builder.Services.Configure<CodeIntelServiceOptions>(builder.Configuration.GetSection("service"));

builder.Services.AddHttpClient(KbIdentityResolver.ClientName);
builder.Services.ConfigureHttpClientDefaults(c => c.ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(10)));
builder.Services.AddSingleton<KbIdentityResolver>(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    var options = sp.GetRequiredService<IOptions<CodeIntelOptions>>().Value;
    var client = factory.CreateClient(KbIdentityResolver.ClientName);
    client.BaseAddress = new Uri(options.KbUrl);
    return new KbIdentityResolver(client, sp.GetRequiredService<IOptions<CodeIntelOptions>>());
});
builder.Services.AddSingleton<WorkspaceManager>();
builder.Services.AddSingleton<PiSharp.CodeIntel.Query.CodeIntelligence>();
builder.Services.AddSingleton<McpEndpoint>();

var app = builder.Build();

// Auth on /mcp and /whoami. /health stays public but carries the seat when the token is valid.
app.UseCodeIntelAuth(new PathString("/mcp"), new PathString("/whoami"));

// /health: 200 with the seat identity (the actor from the resolved token, or null when none was sent).
app.MapGet("/health", (HttpContext ctx, IOptions<CodeIntelOptions> o) => Results.Json(new
{
    status = "ok",
    version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
    seat = ctx.Actor() is { } actor ? new { actor.Slug, actor.Name, actor.Runtime } : null,
    kb = o.Value.KbUrl,
}));

// /whoami: the resolved seat identity (already authenticated by the middleware).
app.MapGet("/whoami", (HttpContext ctx) =>
{
    var actor = ctx.Actor()!;
    return Results.Json(new
    {
        slug = actor.Slug,
        name = actor.Name,
        runtime = actor.Runtime,
        writable_kinds = actor.WritableKinds,
    });
});

// MCP streamable-HTTP.
app.MapPost("/mcp", async (HttpContext ctx, McpEndpoint endpoint, CancellationToken ct) =>
{
    var actor = ctx.Actor()!; // the middleware guarantees a resolved actor on /mcp
    var body = await new StreamReader(ctx.Request.Body).ReadToEndAsync(ct);
    var response = await endpoint.HandlePostAsync(SessionId(ctx), actor, body, ct);
    response.ApplyHeaders(ctx.Response);
    ctx.Response.StatusCode = response.Status;
    ctx.Response.ContentType = response.ContentType ?? "application/json";
    await ctx.Response.WriteAsync(response.Body, ct);
});

app.MapGet("/mcp", async (HttpContext ctx, McpEndpoint endpoint, CancellationToken ct) =>
{
    var actor = ctx.Actor()!;
    var response = await endpoint.HandleGetAsync(SessionId(ctx), actor, ct);
    ctx.Response.StatusCode = response.Status;
    if (response.ContentType is not null) ctx.Response.ContentType = response.ContentType;
    if (response.Body.Length > 0) await ctx.Response.WriteAsync(response.Body, ct);
});

app.MapDelete("/mcp", async (HttpContext ctx, McpEndpoint endpoint, CancellationToken ct) =>
{
    var actor = ctx.Actor()!;
    await endpoint.HandleDeleteAsync(SessionId(ctx), actor, ct);
    ctx.Response.StatusCode = 200;
});

app.Run();

static string? SessionId(HttpContext ctx) =>
    ctx.Request.Headers.TryGetValue("Mcp-Session-Id", out var v) ? v.ToString() : null;

// Test host entry point.
public partial class Program { }
