namespace Holodeck.Web.Settings;

// One agent's settings as Settings shows them. Mode is form when the definition declares a schema, else raw text.
// Revision identifies Content; SchemaRevision identifies the schema. Errors describe the stored content as it is now.
public sealed record SettingsView(string Mode, string? Definition, string Content, string Revision, SettingsSchemaNode? Schema, string? SchemaRevision, string? Notice, bool NeedsRepair, List<SettingsFieldError> Errors);
