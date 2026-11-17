using System.Runtime.InteropServices;

namespace OpenUL.Windowing.GLFW.Imports;

internal static partial class GL
{
  [LibraryImport("libGL.so.1", EntryPoint = "glClearColor")]
  public static partial void ClearColor(
    float red,
    float green,
    float blue,
    float alpha
  );

  [LibraryImport("libGL.so.1", EntryPoint = "glClear")]
  public static partial void Clear(uint mask);

  public const uint ColorBufferBit = 0x00004000;
}