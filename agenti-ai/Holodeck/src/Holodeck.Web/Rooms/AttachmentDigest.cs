namespace Holodeck.Web.Rooms;

// The request's name for a file, and the length and SHA-256 of its bytes.
internal sealed record AttachmentDigest(string Name, long Size, string Sha256);
