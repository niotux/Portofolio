using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Holodeck.Web.Rooms;

// What a keyed send committed to: the resolved channel, the note, and each attachment in request order. Every part
// is length-prefixed, so different sends cannot serialize to the same bytes.
internal static class SendFingerprint
{
  private const string Version = "holodeck-send-v1";

  public static string Compute(string channelId, string note, List<AttachmentDigest> files)
  {
    using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    AppendText(hash, Version);
    AppendText(hash, channelId);
    AppendText(hash, note);
    AppendNumber(hash, files.Count);
    foreach (AttachmentDigest file in files)
    {
      AppendText(hash, file.Name);
      AppendNumber(hash, file.Size);
      AppendText(hash, file.Sha256);
    }

    return Convert.ToHexStringLower(hash.GetHashAndReset());
  }

  private static void AppendText(IncrementalHash hash, string text)
  {
    byte[] bytes = Encoding.UTF8.GetBytes(text);
    AppendNumber(hash, bytes.Length);
    hash.AppendData(bytes);
  }

  private static void AppendNumber(IncrementalHash hash, long value)
  {
    Span<byte> bytes = stackalloc byte[sizeof(long)];
    BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
    hash.AppendData(bytes);
  }
}
