namespace Holodeck.Web.Computers;

// One agent's computer as Team shows it: what it asks for, what it was granted, what can be granted, its disk, and
// whether it may use sudo.
public sealed record ComputerView(AgentNeeds Needs, AgentGrants Grants, List<LibraryServiceView> LibraryServices, List<string> FolderRoots, long WorkspaceBytes, long HomeBytes, bool DiskWarning, bool Sudo);
