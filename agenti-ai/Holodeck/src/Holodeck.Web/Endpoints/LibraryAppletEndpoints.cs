using Holodeck.Web.Projects;
using Holodeck.Web.Rooms;
using Microsoft.AspNetCore.StaticFiles;

namespace Holodeck.Web.Endpoints;

public static class LibraryAppletEndpoints
{
  private const string BridgeAsset = "_holodeck/bridge.js";
  private const string AssetPolicy = "default-src 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self' data:; connect-src 'none'; frame-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'self'; sandbox allow-scripts";
  private static readonly FileExtensionContentTypeProvider ContentTypes = new();

  public static void Map(WebApplication app)
  {
    app.MapPost("/api/projects/{projectId}/activate", (string projectId, ProjectApplications applications, CancellationToken token) => EndpointErrors.RunAsync(async () =>
    {
      ProjectActivationResult result = await applications.ActivateAsync(projectId, token);
      return result.Failures.Count == 0 ? Results.NoContent() : Results.Ok(result);
    }));
    app.MapPost("/api/projects/{projectId}/stop", (string projectId, ProjectApplications applications, CancellationToken token) => EndpointErrors.RunAsync(async () =>
    {
      await applications.StopAsync(projectId, token);
      return Results.NoContent();
    }));
    app.MapGet("/api/projects/{projectId}/applets", (string projectId, ProjectApplications applications) => EndpointErrors.RunAsync(async () => Results.Ok(await applications.AppletsAsync(projectId))));
    app.MapGet("/api/projects/{projectId}/libraries/{libraryId}/{version}/applets/{appletId}/assets/{**path}", AssetAsync);
  }

  private static Task<IResult> AssetAsync(string projectId, string libraryId, string version, string appletId, string? path, ProjectApplications applications, IWebHostEnvironment environment, HttpContext context)
  {
    return EndpointErrors.RunAsync(async () =>
    {
      LibraryApplicationPackage package = await applications.PackageAsync(projectId, libraryId, version);
      LibraryApplet applet = ProjectApplications.Applet(package, appletId);
      string file;
      if (path == BridgeAsset)
      {
        file = Path.Combine(environment.WebRootPath, "library-applet-bridge.js");
      }
      else
      {
        file = LibraryFiles.Inside(package.Directory, path ?? applet.EntryPoint);
        string root = Path.GetDirectoryName(LibraryFiles.Inside(package.Directory, applet.EntryPoint))!;
        if (!file.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
          throw new ArgumentException("Applet assets must stay within their declared entry directory.");
        }
      }

      if (!File.Exists(file))
      {
        throw new KeyNotFoundException("Applet asset was not found.");
      }

      context.Response.Headers.ContentSecurityPolicy = AssetPolicy;
      context.Response.Headers.XContentTypeOptions = "nosniff";
      context.Response.Headers.CacheControl = "no-cache";
      context.Response.Headers.AccessControlAllowOrigin = "*";
      context.Response.Headers["Referrer-Policy"] = "no-referrer";
      if (!ContentTypes.TryGetContentType(file, out string? mediaType))
      {
        mediaType = "application/octet-stream";
      }

      return Results.File(file, mediaType);
    });
  }
}
