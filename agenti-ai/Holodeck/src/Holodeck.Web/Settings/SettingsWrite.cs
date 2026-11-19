namespace Holodeck.Web.Settings;

// A conditional save. BaseRevision and SchemaRevision are what the editor was opened with. Send either Content, the
// whole new text, or Changes to individual form fields, which leave every other value exactly as stored.
public sealed record SettingsWrite(string? BaseRevision, string? SchemaRevision, string? Content, List<SettingsChange>? Changes);
