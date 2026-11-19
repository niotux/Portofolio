using Holodeck.Web.AgentData;
using Holodeck.Web.Rooms;

namespace Holodeck.Web.Endpoints;

// The user's own tools reach an installed library's shared database, the same one its agents use. Holodeck runs the
// SQL as that library's role and never knows what the library keeps there.
public static class LibraryDatabaseEndpoints
{
  public static void Map(WebApplication app)
  {
    app.MapPost("/api/libraries/{libraryId}/{version}/database", RunAsync);
  }

  private static Task<IResult> RunAsync(string libraryId, string version, SqlRequest input, AgentLibrary libraries, AgentDatabases databases, CancellationToken token)
  {
    return EndpointErrors.RunAsync(async () =>
    {
      RequireInstalled(libraries, libraryId, version);
      return Results.Ok(await databases.RunLibraryAsync(libraryId + "/" + version, input, false, token));
    });
  }

  private static void RequireInstalled(AgentLibrary libraries, string libraryId, string version)
  {
    LibraryFiles.RequireId(libraryId, "Library Id");
    LibraryFiles.RequireId(version, "Library Version");
    foreach (LibrarySummary library in libraries.ReadCatalog().Libraries)
    {
      if (library.Id == libraryId && library.Version == version)
      {
        return;
      }
    }

    throw new KeyNotFoundException("No installed library has that identity and version.");
  }
}
