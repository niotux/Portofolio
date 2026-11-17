using System.Numerics;

namespace OpenUL.Mathematics.Vectors;

public interface IImmutableVector4<TSelf, TElem> : IImmutableVector3<TSelf, TElem>
  where TSelf : IImmutableVector4<TSelf, TElem>
  where TElem : INumber<TElem>
{
  static abstract TSelf Create(TElem x, TElem y, TElem z, TElem w);
  static TSelf IImmutableVector3<TSelf, TElem>.Create(TElem x, TElem y, TElem z) => TSelf.Create(x, y, z, TElem.Zero);
  TElem W { get; }
}


public interface IVector4<TSelf, TElem> : IImmutableVector4<TSelf, TElem>, IVector3<TSelf, TElem>
  where TSelf : IVector4<TSelf, TElem>
  where TElem : INumber<TElem>
{
  new TElem W { get; set; }
  TElem IImmutableVector4<TSelf, TElem>.W => Z;
}