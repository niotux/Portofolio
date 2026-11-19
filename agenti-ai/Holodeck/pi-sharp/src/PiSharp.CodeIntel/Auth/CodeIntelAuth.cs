// Bearer-seat-token auth, copied from AiFactory.Chat's UseChatAuth: the KB is the single source
// of identity (a /whoami round trip per token, 60s/10s cache). A missing or invalid token ->
// 401 with WWW-Authenticate; a KB that is unreachable -> 503 (identity provider down), not a
// 401. The resolved actor is stashed in HttpContext.Items for the downstream handlers.
namespace PiSharp.CodeIntel.Auth;

public static class CodeIntelAuth
{
    public const string ItemKey = "codeintel.actor";

    /// <summary>Authenticates the bearer seat token for the given protected paths (e.g. /mcp and
    /// /whoami).</summary>
    public static IApplicationBuilder UseCodeIntelAuth(this IApplicationBuilder app, params PathString[] protectedPaths)
        => app.Use(async (ctx, next) =>
        {
            if (!protectedPaths.Any(p => ctx.Request.Path.StartsWithSegments(p)))
            {
                await next();
                return;
            }
            var token = BearerTokens.FromHeader(ctx.Request.Headers.Authorization);
            CodeIntelActor? actor = null;
            if (token is not null)
            {
                try
                {
                    actor = await ctx.RequestServices.GetRequiredService<KbIdentityResolver>()
                        .ResolveAsync(token, ctx.RequestAborted);
                }
                catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
                {
                    ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("codeintel.auth")
                        .LogWarning(e, "KB identity service unreachable");
                    ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    await ctx.Response.WriteAsJsonAsync(new { error = "The knowledge base (identity provider) is unreachable; try again shortly." });
                    return;
                }
            }
            if (actor is null)
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                ctx.Response.Headers.WWWAuthenticate = "Bearer realm=\"pi-sharp-codeintel\"";
                await ctx.Response.WriteAsJsonAsync(new { error = "Missing or invalid bearer token. Use your knowledge-base actor token." });
                return;
            }
            ctx.Items[ItemKey] = actor;
            await next();
        });

    /// <summary>Reads the resolved actor stashed by <see cref="UseCodeIntelAuth"/>.</summary>
    public static CodeIntelActor? Actor(this HttpContext ctx) =>
        ctx.Items.TryGetValue(ItemKey, out var actor) ? (CodeIntelActor?)actor : null;
}
