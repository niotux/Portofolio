namespace Holodeck.Web.Computers;

// Docker expects {} as the value in some maps, such as exposed ports and network endpoints.
public sealed record EmptyJson();
