namespace Holodeck.Web.Computers;

// The processes of one project library environment the user allowed to use sudo.
internal sealed record LibrarySudoGrants(List<string> Processes);
