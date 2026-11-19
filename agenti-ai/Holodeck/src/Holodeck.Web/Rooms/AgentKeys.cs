using System.Security.Cryptography;
using System.Text;

namespace Holodeck.Web.Rooms;

// A key is an agent's identity for one run. Only its hash is stored.
public static class AgentKeys
{
  private const int KeyBytes = 32;

  public static string Create()
  {
    return Base64Url(RandomNumberGenerator.GetBytes(KeyBytes));
  }

  public static string Hash(string key)
  {
    byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
    return Convert.ToHexStringLower(hash);
  }

  private static string Base64Url(byte[] bytes)
  {
    return System.Buffers.Text.Base64Url.EncodeToString(bytes);
  }
}
