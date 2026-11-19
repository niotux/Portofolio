namespace Holodeck.Web.Computers;

// What the user granted one agent instance beyond its own computer.
public sealed record AgentGrants(List<PortGrant> Ports, List<FolderGrant> Folders);
