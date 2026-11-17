using System.Numerics;

namespace OpenUL.Mathematics.Vectors;

public interface IImmutableVector3<TSelf, TElem> : IImmutableVector2<TSelf, TElem>
  where TSelf : IImmutableVector3<TSelf, TElem>
  where TElem : INumber<TElem>
{
  static abstract TSelf Create(TElem x, TElem y, TElem z);
  static TSelf IImmutableVector2<TSelf, TElem>.Create(TElem x, TElem y) => TSelf.Create(x, y, TElem.Zero);
  TElem Z { get; }
}


public interface IVector3<TSelf, TElem> : IImmutableVector3<TSelf, TElem>, IVector2<TSelf, TElem>
  where TSelf : IVector3<TSelf, TElem>
  where TElem : INumber<TElem>
{
  new TElem Z { get; set; }
  TElem IImmutableVector3<TSelf, TElem>.Z => Z;
}