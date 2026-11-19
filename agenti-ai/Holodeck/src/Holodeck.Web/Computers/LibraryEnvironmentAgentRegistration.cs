namespace Holodeck.Web.Computers;

public sealed record LibraryEnvironmentAgentRegistration(string Id, List<string> Command, Dictionary<string, string> Environment, int PlatformGid, string SettingsJson);
