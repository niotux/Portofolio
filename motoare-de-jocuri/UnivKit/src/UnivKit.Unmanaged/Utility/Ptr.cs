using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace UnivKit.Unmanaged.Utility;

[StructLayout(LayoutKind.Sequential)]
public unsafe readonly struct Ptr<T>
  where T : unmanaged
{
  private Ptr(T* adress)
  {
    this.adress = adress;
  }

  private readonly T* adress;
  public ref T Value => ref *this.adress;

  public static implicit operator T*(Ptr<T> ptr)
  {
    return ptr.adress;
  }

  public static implicit operator Ptr<T>(T* ptr)
  {
    return new(ptr);
  }
}

public static class Ptr
{
  [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.AggressiveInlining)]
  public unsafe static Ptr<T> From<T>(ref T value)
    where T : unmanaged
  {
    Ptr<T> ptr;
    fixed (T* adress = &value)
    {
      ptr = adress;
    }
    return ptr;
  }
}
