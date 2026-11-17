using System.Runtime.InteropServices;

namespace UnivKit.OS.Platform.Windows;

public static partial class GDI32
{
  public const string LibName = "gdi32.dll";


  [LibraryImport(LibName)]
  public static partial IntPtr GetStockObject(User32.StockObject fnObject);
}