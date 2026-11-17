using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using PrimordialPower.Mathematics.Matrices;

namespace PrimordialPower.Mathematics.Vectors;

/// <summary>
/// Contains utility functions for vector operations 
/// </summary>
public static partial class Vector
{
  private static volatile uint _fastSqrtNewtonRaphsonIterations = 2;
  public static uint FastSqrtNewtonRaphsonIterations
  {
    get => _fastSqrtNewtonRaphsonIterations;
    set => _fastSqrtNewtonRaphsonIterations = value;
  }



  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static TVector Normalize<TVector>(in TVector vector)
  where TVector : IVectorFloating<TVector>
  => vector.Normalized;



  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static TVector NormalizeFast<TVector>(in TVector vector)
  where TVector : IVectorFloatingIeee754<TVector>
  => vector.NormalizedFast;



  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static TVector Add<TVector>(in TVector left, in TVector right)
  where TVector : IVector<TVector>
  => left.Add(right);



  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static TVector Subtract<TVector>(in TVector left, in TVector right)
  where TVector : IVector<TVector>
  => left.Subtract(right);



  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static TValue Dot<TValue, TVector>(in TVector left, in TVector right)
  where TVector : IVector<TVector, TValue>
  where TValue : INumber<TValue>
  => left.Dot(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static float DotF<TVector>(in TVector left, in TVector right)
  where TVector : IVector<TVector, float>
  => left.Dot(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static int DotI<TVector>(in TVector left, in TVector right)
  where TVector : IVector<TVector, int>
  => left.Dot(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static double DotD<TVector>(in TVector left, in TVector right)
  where TVector : IVector<TVector, double>
  => left.Dot(right);



  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static TVector Multiply<TVector, TValue>(in TVector left, TValue right)
  where TVector : IVector<TVector, TValue>
  where TValue : INumber<TValue>
  => left.Multiply(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static TVector Multiply<TVector, TValue>(TValue left, in TVector right)
  where TVector : IVector<TVector, TValue>
  where TValue : INumber<TValue>
  => right.Multiply(left);

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static TVector Multiply<TVector>(in TVector left, in TVector right)
  where TVector : IVector<TVector>
  => left.Multiply(right);



  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static TVector Divide<TVector>(in TVector left, in TVector right)
  where TVector : IVector<TVector>
  => left.Divide(right);



  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static TResult Cross<TVector, TResult>(in TVector left, in TVector right)
  where TVector : IVectorHasCross<TVector, TResult>
  where TResult : IVector<TResult>
  => left.Cross(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static Vec3<TVal> Cross<TVal>(in Vec2<TVal> left, in Vec2<TVal> right)
  where TVal : INumber<TVal>, new()
  => left.Cross(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static Vec3<TVal> Cross<TVal>(in Vec3<TVal> left, in Vec3<TVal> right)
  where TVal : INumber<TVal>, new()
  => left.Cross(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static Vec3F CrossF<TVector>(in TVector left, in TVector right)
  where TVector : IVectorHasCross<TVector, Vec3F>
  => left.Cross(right);

  public static Vec3I CrossI<TVector>(in TVector left, in TVector right)
  where TVector : IVectorHasCross<TVector, Vec3I>
  => left.Cross(right);

  public static Vec3D CrossD<TVector>(in TVector left, in TVector right)
  where TVector : IVectorHasCross<TVector, Vec3D>
  => left.Cross(right);



  public static string ToString<TVector, TValue>(in TVector vector)
  where TVector : IVector<TVector, TValue>
  where TValue : INumber<TValue>
  {
    if (TVector.DimCount == 0) return "(null)";

    string open = "(";
    string close = ")";
    string separator = ", ";
    StringBuilder str = new(1 + (int)TVector.DimCount * 2);

    str.Append(open);

    for (int i = 0; i < TVector.DimCount - 1; i++)
    {
      str.Append(vector[i]);
      str.Append(separator);
    }
    str.Append(vector[(int)TVector.DimCount - 1]);

    str.Append(close);

    return str.ToString();
  }
}
