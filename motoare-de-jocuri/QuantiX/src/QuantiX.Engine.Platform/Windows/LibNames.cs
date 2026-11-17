using System.Runtime.Versioning;

namespace QuantiX.Engine.Platform.Windows;

[SupportedOSPlatform("windows")]
public static class LibNames
{
  public const string User = "user32.dll";
  public const string Kernel = "kernel32.dll";
  public const string DWMAPI = "dwmapi.dll";
  public const string GDI32 = "gdi32.dll";
  public const string AVRT = "avrt.dll";
  public const string OLE32 = "ole32.dll";
}