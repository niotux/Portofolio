using System.Runtime.InteropServices;

namespace UnivKit.OS.Platform.Windows;

public static partial class DWMApi
{
  public const string LibName = "dwmapi.dll";


  [LibraryImport(LibName)]
  public static partial int DwmSetWindowAttribute(IntPtr hwnd, DWMWA attr, ref int attrValue, int attrSize);


  // DWM Window Attributes
  public enum DWMWA : int
  {
    USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19,
    USE_IMMERSIVE_DARK_MODE = 20,
    WINDOW_CORNER_PREFERENCE = 33,
  }


  // DWM Window Corner Preference
  public enum DWMWCP : int
  {
    DEFAULT = 0,
    DONOTROUND = 1,
    ROUND = 2,
    ROUNDSMALL = 3,
  }


  // DWM Dark Mode
  public enum DWMDarkMode : int
  {
    LIGHT = 0,
    DARK = 1,
  }
}
