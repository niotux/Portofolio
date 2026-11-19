namespace Holodeck.Web.Computers;

// Docker Engine API network create body: property names are Docker's.
public sealed record NetworkCreateRequest(string Name, bool Internal, Dictionary<string, string> Labels);
