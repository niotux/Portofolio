namespace Holodeck.Web.Settings;

// Where an agent's settings rules come from: its definition's schema, or none. Notice explains a missing definition.
internal sealed record SettingsSource(SettingsSchema? Schema, string? Notice);
