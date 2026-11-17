using System.Numerics;
using System.Runtime.CompilerServices;

namespace OpenUL.Mathematics.Vectors;

public static class Vec
{
  public static TVec Add<TVec>(TVec a, TVec b)
  where TVec : IVector<TVec>
    => a + b;

  public static TVec Subtract<TVec>(TVec a, TVec b)
  where TVec : IVector<TVec>
    => a + b;

  public static TVec Multiply<TVec>(TVec a, TVec b)
  where TVec : IVector<TVec>
    => a + b;

  public static TVec Divide<TVec>(TVec a, TVec b)
  where TVec : IVector<TVec>
    => a + b;

  public static TVec Cross<TVec>(TVec a, TVec b)
  where TVec : IVectorCross<TVec, TVec>
    => TVec.Cross(a, b);

  public static TVecResult Cross<TVec, TVecResult>(TVec a, TVec b)
  where TVec : IVectorCross<TVec, TVecResult>
  where TVecResult : IVector<TVecResult>
    => TVec.Cross(a, b);

}

public static class Vec<T>
where T : INumber<T>
{
  public static T Dot<TVec>(TVec a, TVec b, NumberCreation creation = NumberCreation.Saturate)
  where TVec : IVector<TVec>
    => TVec.Dot<T>(a, b, creation);
}

public static class Vector2Ext
{
  public static void GetTuple<TVec, TElem>(this TVec vec, out (TElem X, TElem Y) tuple)
  where TVec : IImmutableVector2<TVec, TElem>
  where TElem : INumber<TElem>
    => tuple = (vec.X, vec.Y);


  public static void GetVector<TVec, TElem>(this (TElem X, TElem Y) tuple, out TVec vec)
  where TVec : IImmutableVector2<TVec, TElem>
  where TElem : INumber<TElem>
    => vec = TVec.Create(tuple.X, tuple.Y);


  extension<TVec, TElem>(TVec v)
  where TVec : IImmutableVector2<TVec, TElem>
  where TElem : INumber<TElem>
  {
    public static TVec Create((TElem X, TElem Y) tuple)
      => TVec.Create(tuple.X, tuple.Y);

    public static TVec Create(TElem value)
    => TVec.Create(value, value);
  }
}

public static class Vector3Ext
{

  public static void GetTuple<TVec, TElem>(this TVec vec, out (TElem X, TElem Y, TElem Z) tuple)
  where TVec : IImmutableVector3<TVec, TElem>
  where TElem : INumber<TElem>
    => tuple = (vec.X, vec.Y, vec.Z);


  public static void GetVector<TVec, TElem>(this (TElem X, TElem Y, TElem Z) tuple, out TVec vec)
  where TVec : IImmutableVector3<TVec, TElem>
  where TElem : INumber<TElem>
    => vec = TVec.Create(tuple.X, tuple.Y, tuple.Z);


  extension<TVec, TElem>(TVec v)
  where TVec : IImmutableVector3<TVec, TElem>
  where TElem : INumber<TElem>
  {
    public static TVec Create((TElem X, TElem Y, TElem Z) tuple)
      => TVec.Create(tuple.X, tuple.Y, tuple.Z);
  }
}

public static class Vector4Ext
{
  public static void GetTuple<TVec, TElem>(this TVec vec, out (TElem X, TElem Y, TElem Z, TElem W) tuple)
  where TVec : IImmutableVector4<TVec, TElem>
  where TElem : INumber<TElem>
    => tuple = (vec.X, vec.Y, vec.Z, vec.W);


  public static void GetVector<TVec, TElem>(this (TElem X, TElem Y, TElem Z, TElem W) tuple, out TVec vec)
  where TVec : IImmutableVector4<TVec, TElem>
  where TElem : INumber<TElem>
    => vec = TVec.Create(tuple.X, tuple.Y, tuple.Z, tuple.W);


  extension<TVec, TElem>(TVec v)
  where TVec : IImmutableVector4<TVec, TElem>
  where TElem : INumber<TElem>
  {
    public static TVec Create((TElem X, TElem Y, TElem Z, TElem W) tuple)
      => TVec.Create(tuple.X, tuple.Y, tuple.Z, tuple.W);
  }
}