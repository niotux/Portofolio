using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PrimordialPower.Mathematics;

public static partial class MathExt
{
  [StructLayout(LayoutKind.Explicit)]
  private struct DoubleBytes
  {
    [FieldOffset(0)]
    public double AsDouble;

    [FieldOffset(0)]
    public ulong AsULong;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  private static (double B, double AHalf) _FastInvSqrtCore(double a)
  {
    var doubleBytes = new DoubleBytes();
    doubleBytes.AsDouble = a;

    double ahalf = 0.5d * a;
    doubleBytes.AsULong = 0x5fe6eb50c7b537a9 - (doubleBytes.AsULong >> 1);
    double b = doubleBytes.AsDouble;

    return (b, ahalf);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static double FastInvSqrt(double a)
  {
    var result = _FastInvSqrtCore(a);

    result.B *= 1.5d - result.AHalf * result.B * result.B;

    return result.B;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static double FastInvSqrt(double a, uint newtonRaphsonIterations)
  {
    var result = _FastInvSqrtCore(a);

    for (uint i = 0u; i < newtonRaphsonIterations; i++)
    {
      result.B *= 1.5d - result.AHalf * result.B * result.B;
    }

    return result.B;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static double FastSqrt(double a) => 1.0 / FastInvSqrt(a);


  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static double FastSqrt(double a, uint newtonRaphsonIterations) => 1.0 / FastInvSqrt(a, newtonRaphsonIterations);



  [StructLayout(LayoutKind.Explicit)]
  private struct FloatBytes
  {
    [FieldOffset(0)]
    public float AsFloat;

    [FieldOffset(0)]
    public uint AsUInt;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  private static (float B, float AHalf) _FastInvSqrtCore(float a)
  {
    var floatBytes = new FloatBytes();
    floatBytes.AsFloat = a;

    float ahalf = 0.5f * a;
    floatBytes.AsUInt = 0x5f3759df - (floatBytes.AsUInt >> 1);
    float b = floatBytes.AsFloat;

    return (b, ahalf);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static float FastInvSqrt(float a)
  {
    var result = _FastInvSqrtCore(a);

    result.B *= 1.5f - result.AHalf * result.B * result.B;

    return result.B;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static float FastInvSqrt(float a, uint newtonRaphsonIterations)
  {
    var result = _FastInvSqrtCore(a);

    for (uint i = 0u; i < newtonRaphsonIterations; i++)
    {
      result.B *= 1.5f - result.AHalf * result.B * result.B;
    }

    return result.B;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static float FastSqrt(float a) => 1f / FastInvSqrt(a);


  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static float FastSqrt(float a, uint newtonRaphsonIterations) => 1f / FastInvSqrt(a, newtonRaphsonIterations);
}
