using System.Runtime.Versioning;

namespace QuantiX.Engine.Platform.Linux;

[SupportedOSPlatform("linux")]
public static class LibNames
{
  public const string X11 = "libX11.so.6";
  public const string ASound = "libasound.so.2";
  public const string GL = "libGL.so.1";
}