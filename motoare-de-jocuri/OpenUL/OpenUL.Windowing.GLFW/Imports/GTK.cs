using System.Runtime.InteropServices;
using Silk.NET.Core.Attributes;

namespace OpenUL.Windowing.GLFW.Imports;

internal static partial class GTK
{
  public const string LibName = "libgtk-3.so.0";
  private static nint Handle;
  public static bool IsAvailable { get; } = NativeLibrary.TryLoad(LibName, out Handle);
  public static void Dispose() => NativeLibrary.Free(Handle);

  [LibraryImport(LibName, EntryPoint = "gtk_init_check")]
  public static partial int InitCheck(ref int argc, ref IntPtr argv);

  [LibraryImport(LibName, EntryPoint = "gtk_init_check")]
  public static partial int InitCheckSimple(IntPtr argc, IntPtr argv);
}