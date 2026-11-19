using Holodeck.Web.Computers;
using Holodeck.Web.Settings;
using Holodeck.Web.Management;

namespace Holodeck.Web.Rooms;

// A packaged agent: Folder is its versioned reference, Start its launch command and arguments. SettingsSchema is set
// when the definition declares one; its settings are then edited through a form and validated.
public sealed record LibraryAgent(string Folder, string Name, string Description, List<string> Start, string Directory, string LibraryDirectory, AgentNeeds Needs, string? SettingsPath = null, SettingsSchema? SettingsSchema = null, ManagementDefinition? Actions = null, string LibraryKey = "", List<LibraryMount>? Dependencies = null, LibraryEnvironmentDefinition? Runtime = null);
