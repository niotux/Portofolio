using System;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;

namespace QuantiX.Engine.Assets;

public static class AssetsProvider
{
  public static byte[] LoadBytes(this IAssetsProvider self, string path)
  {
    using var stream = self.GetFile(path, FileMode.Open);
    var buffer = new byte[stream.Length];
    stream.Read(buffer, 0, buffer.Length);
    return buffer;
  }

  public static string LoadText(this IAssetsProvider self, string path)
  {
    using var stream = self.GetFile(path, FileMode.Open);
    Span<byte> buffer = stream.Length < 4096
      ? stackalloc byte[(int)stream.Length]
      : new byte[stream.Length];
    stream.Read(buffer);
    var chars = MemoryMarshal.Cast<byte, char>(buffer);
    return new string(chars);
  }

  public static void SaveBytes(this IAssetsProvider self, string path, byte[] data)
  {
    using var stream = self.GetFile(path, FileMode.Open);
    stream.Write(data, 0, data.Length);
  }

  public static void SaveText(this IAssetsProvider self, string path, string data)
  {
    using var stream = self.GetFile(path, FileMode.Open);
    var chars = MemoryMarshal.Cast<char, byte>(data.AsSpan());
    stream.Write(chars);
  }
}
