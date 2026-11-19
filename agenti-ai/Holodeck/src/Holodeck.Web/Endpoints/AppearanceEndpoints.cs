using Holodeck.Web.Appearance;

namespace Holodeck.Web.Endpoints;

public static class AppearanceEndpoints
{
  public static void Map(WebApplication app)
  {
    app.MapGet("/api/appearance", (AppearanceStore store, CancellationToken token) => EndpointErrors.RunAsync(async () => Results.Ok(await store.ReadAsync(token))));
    app.MapPut("/api/appearance", WriteAsync);
  }

  private static Task<IResult> WriteAsync(AppearanceDocument document, AppearanceStore store, CancellationToken token)
  {
    return EndpointErrors.RunAsync(async () =>
    {
      await store.WriteAsync(document, token);
      return Results.NoContent();
    });
  }
}
