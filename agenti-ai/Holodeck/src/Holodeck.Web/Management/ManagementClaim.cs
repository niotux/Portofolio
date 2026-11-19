namespace Holodeck.Web.Management;

public sealed record ManagementClaim(ManagementJob Job, string SettingsContent, string AttemptToken, DateTimeOffset LeaseExpiresAt);
