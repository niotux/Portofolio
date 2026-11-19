using System.Security.Cryptography;

namespace Holodeck.Web.Rooms;

// Reads attachment bytes once, hashing what is actually read, so a fingerprint describes the stored copy rather
// than whatever the source file holds a moment later.
internal static class Attachments
{
  private const int BufferBytes = 81920;

  public static async Task<AttachmentDigest> CopyAsync(OutgoingFile file, string target, CancellationToken token)
  {
    await using FileStream source = OpenSource(file);
    await using FileStream copy = new(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
    return await ReadAsync(file, source, copy, token);
  }

  public static async Task<AttachmentDigest> DigestAsync(OutgoingFile file, CancellationToken token)
  {
    await using FileStream source = OpenSource(file);
    return await ReadAsync(file, source, null, token);
  }

  private static FileStream OpenSource(OutgoingFile file)
  {
    if (file.Table is not null)
    {
      return TableFiles.OpenRead(file.Table, file.SourcePath, file.Name);
    }

    return new FileStream(file.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
  }

  private static async Task<AttachmentDigest> ReadAsync(OutgoingFile file, FileStream source, FileStream? copy, CancellationToken token)
  {
    using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    byte[] buffer = new byte[BufferBytes];
    long size = 0;
    while (true)
    {
      int read = await source.ReadAsync(buffer, token);
      if (read == 0)
      {
        break;
      }

      size += read;
      if (size > RoomLimits.MaxFileBytes)
      {
        throw new ArgumentException($"'{file.Name}' is larger than {RoomLimits.MaxFileBytes / (1024 * 1024)} MB.");
      }

      hash.AppendData(buffer, 0, read);
      if (copy is not null)
      {
        await copy.WriteAsync(buffer.AsMemory(0, read), token);
      }
    }

    return new AttachmentDigest(file.Name, size, Convert.ToHexStringLower(hash.GetHashAndReset()));
  }
}
