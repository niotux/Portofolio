using System.Text.Json;

namespace Holodeck.Web.Appearance;

// The app's appearance settings. The document belongs to the browser; the server only stores it.
public sealed record AppearanceDocument(JsonElement? Settings);
