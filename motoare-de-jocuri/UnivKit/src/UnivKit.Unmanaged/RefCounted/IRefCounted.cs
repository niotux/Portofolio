using System.Numerics;
using System.Runtime.CompilerServices;

namespace UnivKit.Unmanaged.RefCounted;

public interface IRefCounted<TSelf> : IDisposable, IEquatable<TSelf>, IEqualityOperators<TSelf, TSelf, bool>
  where TSelf : unmanaged, IRefCounted<TSelf>
{
  public static abstract TSelf operator ++(TSelf refObject);
  public static abstract TSelf operator --(TSelf refObject);

  public long RefCount { get; }
}

public static class RefCountedObject
{
  public static T Pass<T>(this T o)
    where T : unmanaged, IRefCounted<T>
  {
    return o++;
  }
}