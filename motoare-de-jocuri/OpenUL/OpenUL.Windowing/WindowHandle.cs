using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace OpenUL.Windowing;

public interface IWindowHandle<TSelf>
  where TSelf : IWindowHandle<TSelf>
{
  static abstract TSelf FromWindowHandle(ulong loWord, ulong hiWord);
  static abstract implicit operator WindowHandle(TSelf handle);
}

public readonly record struct WindowHandle() : IEquatable<WindowHandle>, IEqualityOperators<WindowHandle, WindowHandle, bool>
{
  private WindowHandle(Type type, ulong lo, ulong hi)
  : this()
  {
    Type = type;
    LoWord = lo;
    HiWord = hi;
  }

  private readonly Type? Type;
  private readonly ulong LoWord;
  private readonly ulong HiWord;

  public THandle? TryGet<THandle>()
  where THandle : struct, IWindowHandle<THandle>
  {
    if (typeof(THandle) != Type)
    {
      return null;
    }
    return THandle.FromWindowHandle(LoWord, HiWord);
  }

  public static WindowHandle Create<THandle>(ulong loWord, ulong hiWord)
  where THandle : IWindowHandle<THandle>
  {
    return new(typeof(THandle), loWord, hiWord);
  }
}