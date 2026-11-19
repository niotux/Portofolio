namespace Holodeck.Web.Projects;

// One declared library service in a project, as the Services applet shows it. It runs as the Linux user named by its
// Id. Uid and StartedAt are known only while its environment runs.
public sealed record ProjectServiceView(string LibraryId, string LibraryVersion, string LibraryName, string Id, int? Port, bool Running, int? ExitCode, string StartedAt, int? Uid, bool Sudo);
