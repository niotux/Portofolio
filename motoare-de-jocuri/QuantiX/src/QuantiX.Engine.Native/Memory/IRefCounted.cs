using System.Numerics;

namespace QuantiX.Engine.Native.Memory;

public interface IRefCounted<TSelf> : IDisposable, IEquatable<TSelf>, IEqualityOperators<TSelf, TSelf, bool>
  where TSelf : IRefCounted<TSelf>
{
  public TSelf Pass();
  public static abstract TSelf Null { get; }
}