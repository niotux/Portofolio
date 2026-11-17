using System.Numerics;

namespace QuantiX.Engine.Mathematics.Vectors;

[GenerateVector(4)]
public partial record struct Vec4<T> where T : INumberBase<T>;

[GenerateVector(3, SimdSlots = 4, HasCross = true)]
public partial record struct Vec3Pad4<T> where T : INumberBase<T>;

[GenerateVector(3, HasCross = true)]
public partial record struct Vec3<T> where T : INumberBase<T>;

[GenerateVector(2, HasCross = true, CrossResult = "Vec3")]
public partial record struct Vec2<T> where T : INumberBase<T>;



public static class Vector
{
  public static TVec Add<TVec>(in TVec left, in TVec right)
    where TVec : IVector<TVec>
  {
    return TVec.Add(left, right);
  }

  public static TVec Subtract<TVec>(in TVec left, in TVec right)
    where TVec : IVector<TVec>
  {
    return TVec.Subtract(left, right);
  }

  public static TVec Multiply<TVec>(in TVec left, in TVec right)
    where TVec : IVector<TVec>
  {
    return TVec.Multiply(left, right);
  }

  public static TVec Divide<TVec>(in TVec left, in TVec right)
    where TVec : IVector<TVec>
  {
    return TVec.Divide(left, right);
  }

  public static TScalar Dot<TVec, TScalar>(in TVec left, in TVec right)
    where TVec : IVector<TVec>, IVectorOfScalar<TVec, TScalar>
    where TScalar : INumberBase<TScalar>
  {
    return TVec.Dot(left, right);
  }

  public static TVec Cross<TVec>(in TVec left, in TVec right)
    where TVec : IVector<TVec>, IVectorWithCrossProduct<TVec, TVec>
  {
    return TVec.Cross(left, right);
  }

  public static TResult Cross<TVec, TResult>(in TVec left, in TVec right)
  where TVec : IVector<TVec>, IVectorWithCrossProduct<TVec, TResult>
  where TResult : IVector<TResult>
  {
    return TVec.Cross(left, right);
  }
}
