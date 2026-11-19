namespace Holodeck.Web.Computers;

// A host folder mounted into the agent's computer at /mnt/<Name>.
public sealed record FolderGrant(string Name, string HostPath, bool ReadOnly);
