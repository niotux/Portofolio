using Holodeck.Web.Settings;

namespace Holodeck.Web.Management;

public sealed record ManagementAction(string Id, string Title, string Description, SettingsSchemaNode InputSchema, SettingsSchemaNode ResultSchema, List<string> FollowUpActions);
