using System.Security.Cryptography;
using System.Text;

namespace CreativeStudio;

public static class StableIdentity
{
  public static string For(string value)
  {
    byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(value));
    return new Guid(digest.AsSpan(0, 16)).ToString();
  }
}
