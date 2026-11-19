namespace Holodeck.Web.Rooms;

// SettingsSchema describes Settings. Actions names the library-owned management action and schema declarations.
internal sealed record PackageAgentManifest(string? Id, string? Name, string? Description, AgentLaunch? Launch, string? Settings, AgentNeedsManifest? Needs = null, string? SettingsSchema = null, string? Actions = null);
