namespace Holodeck.Web.Management;

public sealed record ManagementDefinition(string DefinitionRevision, List<ManagementAction> Actions);
