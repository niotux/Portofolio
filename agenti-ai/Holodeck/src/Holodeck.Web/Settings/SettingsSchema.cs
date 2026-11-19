namespace Holodeck.Web.Settings;

// A definition's parsed settings schema. Revision is the SHA-256 of the schema file, so any edit to it is visible.
public sealed record SettingsSchema(SettingsSchemaNode Root, string Revision);
