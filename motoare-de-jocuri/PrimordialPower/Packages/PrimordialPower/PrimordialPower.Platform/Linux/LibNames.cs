using System.Runtime.Versioning;

namespace PrimordialPower.Platform.Linux;

[SupportedOSPlatform("linux")]
internal static class LibNames
{
  public const string X11 = "libX11.so.6";
  public const string ASound = "libasound.so.2";
}