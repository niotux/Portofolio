using System.Runtime.InteropServices;

namespace UnivKit.OS.Windowing;

internal static class AtomicHelpers
{
  [StructLayout(LayoutKind.Explicit)]
  public struct Word64
  {
    [FieldOffset(0)]
    public long Word;
    [FieldOffset(0)]
    public int Low;
    [FieldOffset(sizeof(int))]
    public int High;
  }

  [StructLayout(LayoutKind.Explicit)]
  public struct UWord64
  {
    [FieldOffset(0)]
    public ulong Word;
    [FieldOffset(0)]
    public uint Low;
    [FieldOffset(sizeof(int))]
    public uint High;
  }
}