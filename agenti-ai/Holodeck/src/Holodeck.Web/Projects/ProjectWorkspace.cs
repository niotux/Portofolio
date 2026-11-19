using System.Text.Json;

namespace Holodeck.Web.Projects;

// A project's window layout. The document belongs to the browser; the server only stores it.
public sealed record ProjectWorkspace(JsonElement? Layout);
