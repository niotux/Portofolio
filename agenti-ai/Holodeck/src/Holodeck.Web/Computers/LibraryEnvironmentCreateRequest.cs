namespace Holodeck.Web.Computers;

public sealed record LibraryEnvironmentCreateRequest(string Image, List<string> Env, string User, Dictionary<string, string> Labels, LibraryEnvironmentHostConfig HostConfig);
