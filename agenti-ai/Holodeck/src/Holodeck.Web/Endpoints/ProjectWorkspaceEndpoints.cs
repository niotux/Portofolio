using Holodeck.Web.Projects;

namespace Holodeck.Web.Endpoints;

public static class ProjectWorkspaceEndpoints
{
  public static void Map(WebApplication app)
  {
    app.MapGet("/api/projects/{projectId}/workspace", (string projectId, ProjectWorkspaceStore store, CancellationToken token) => EndpointErrors.RunAsync(async () => Results.Ok(await store.ReadAsync(projectId, token))));
    app.MapPut("/api/projects/{projectId}/workspace", WriteAsync);
  }

  private static Task<IResult> WriteAsync(string projectId, ProjectWorkspace workspace, ProjectWorkspaceStore store, CancellationToken token)
  {
    return EndpointErrors.RunAsync(async () =>
    {
      await store.WriteAsync(projectId, workspace, token);
      return Results.NoContent();
    });
  }
}
