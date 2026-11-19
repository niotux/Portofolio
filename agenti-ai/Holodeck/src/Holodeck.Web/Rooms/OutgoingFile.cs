namespace Holodeck.Web.Rooms;

// SourcePath is the absolute path of the bytes to copy into the packet. Table is set when the bytes come from an
// agent's table, which the agent can change at any time; they are then opened only beneath it, never through a link.
public sealed record OutgoingFile(string Name, string SourcePath, string? Table = null);
