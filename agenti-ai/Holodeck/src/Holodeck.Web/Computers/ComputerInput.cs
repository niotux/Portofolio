namespace Holodeck.Web.Computers;

// The grants the user wants; host ports for Ports are assigned by Holodeck.
public sealed record ComputerInput(List<int>? Ports, List<FolderGrant>? Folders);
