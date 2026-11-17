namespace UnivKit.OS.Platform.Windows;

public static class Macros
{

  public static ushort LoWord(IntPtr value) => (ushort)(value.ToInt64() & 0xFFFF);
  public static ushort HiWord(IntPtr value) => (ushort)((value.ToInt64() >> 16) & 0xFFFF);

  public static int SignedLoWord(IntPtr value) => (int)(short)LoWord(value);
  public static int SignedHiWord(IntPtr value) => (int)(short)HiWord(value);
}