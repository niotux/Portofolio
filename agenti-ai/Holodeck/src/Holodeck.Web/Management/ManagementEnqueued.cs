namespace Holodeck.Web.Management;

public sealed record ManagementEnqueued(ManagementJob Job, bool IsReplay);
