using System.Numerics;
using System.Runtime.InteropServices;

namespace OpenUL.Mathematics.Vectors;

public interface IImmutableVector2<TSelf, TElem> : IVector<TSelf, TElem>
  where TSelf : IImmutableVector2<TSelf, TElem>
  where TElem : INumber<TElem>
{
  static abstract TSelf Create(TElem x, TElem y);
  TElem X { get; }
  TElem Y { get; }
}


public interface IVector2<TSelf, TElem> : IImmutableVector2<TSelf, TElem>
  where TSelf : IVector2<TSelf, TElem>
  where TElem : INumber<TElem>
{
  new TElem X { get; set; }
  new TElem Y { get; set; }

  TElem IImmutableVector2<TSelf, TElem>.X => X;
  TElem IImmutableVector2<TSelf, TElem>.Y => Y;
}