namespace Holodeck.Web.Computers;

public sealed record LibraryEnvironmentView(string ContainerId, string Address, List<LibraryEnvironmentProcessView> Processes);
