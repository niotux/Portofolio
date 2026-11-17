using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace QuantiX.Engine.Mathematics.Vectors;

[StructLayout(LayoutKind.Sequential)]
public partial record struct Vec4<T> : IVector<Vec4<T>>, IVectorOfScalar<Vec4<T>, T>, IVecXComponent<T>, IVecYComponent<T>, IVecZComponent<T>, IVecWComponent<T>
{
  private T x;
  private T y;
  private T z;
  private T w;

  public Vec4(T val) : this(val, val, val, val) { }
  public Vec4(T x, T y, T z, T w)
  {
    this.x = x;
    this.y = y;
    this.z = z;
    this.w = w;
  }

  public T X { get => this.x; set => this.x = value; }
  public T Y { get => this.y; set => this.y = value; }
  public T Z { get => this.z; set => this.z = value; }
  public T W { get => this.w; set => this.w = value; }

  public T LengthSquared => Dot(this, this);

  public static readonly Vec4<T> Identity = new(T.One);
  public static readonly Vec4<T> Zero = new(T.Zero);
  public const int Dimensions = 4;

  static Vec4<T> IVector<Vec4<T>>.Identity => Identity;
  static Vec4<T> IVector<Vec4<T>>.Zero => Zero;
  static int IVector<Vec4<T>>.Dimensions => Dimensions;

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static T DistanceSquared(in Vec4<T> vec1, in Vec4<T> vec2) => Subtract(vec1, vec2).LengthSquared;

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec4<T> Add(in Vec4<T> left, in Vec4<T> right)
  {
    if (Unsafe.SizeOf<T>() == 2 && Vector64.IsHardwareAccelerated)
    {
      return FromV64(Vector64.Add(AsV64(left), AsV64(right)));
    }
    if (Unsafe.SizeOf<T>() == 4 && Vector128.IsHardwareAccelerated)
    {
      return FromV128(Vector128.Add(AsV128(left), AsV128(right)));
    }
    if (Unsafe.SizeOf<T>() == 8 && Vector256.IsHardwareAccelerated)
    {
      return FromV256(Vector256.Add(AsV256(left), AsV256(right)));
    }
    return new(left.x + right.x, left.y + right.y, left.z + right.z, left.w + right.w);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec4<T> Subtract(in Vec4<T> left, in Vec4<T> right)
  {
    if (Unsafe.SizeOf<T>() == 2 && Vector64.IsHardwareAccelerated)
    {
      return FromV64(Vector64.Subtract(AsV64(left), AsV64(right)));
    }
    if (Unsafe.SizeOf<T>() == 4 && Vector128.IsHardwareAccelerated)
    {
      return FromV128(Vector128.Subtract(AsV128(left), AsV128(right)));
    }
    if (Unsafe.SizeOf<T>() == 8 && Vector256.IsHardwareAccelerated)
    {
      return FromV256(Vector256.Subtract(AsV256(left), AsV256(right)));
    }
    return new(left.x - right.x, left.y - right.y, left.z - right.z, left.w - right.w);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec4<T> Multiply(in Vec4<T> left, in Vec4<T> right)
  {
    if (Unsafe.SizeOf<T>() == 2 && Vector64.IsHardwareAccelerated)
    {
      return FromV64(Vector64.Multiply(AsV64(left), AsV64(right)));
    }
    if (Unsafe.SizeOf<T>() == 4 && Vector128.IsHardwareAccelerated)
    {
      return FromV128(Vector128.Multiply(AsV128(left), AsV128(right)));
    }
    if (Unsafe.SizeOf<T>() == 8 && Vector256.IsHardwareAccelerated)
    {
      return FromV256(Vector256.Multiply(AsV256(left), AsV256(right)));
    }
    return new(left.x * right.x, left.y * right.y, left.z * right.z, left.w * right.w);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec4<T> Divide(in Vec4<T> left, in Vec4<T> right)
  {
    if (Unsafe.SizeOf<T>() == 2 && Vector64.IsHardwareAccelerated)
    {
      return FromV64(Vector64.Divide(AsV64(left), AsV64(right)));
    }
    if (Unsafe.SizeOf<T>() == 4 && Vector128.IsHardwareAccelerated)
    {
      return FromV128(Vector128.Divide(AsV128(left), AsV128(right)));
    }
    if (Unsafe.SizeOf<T>() == 8 && Vector256.IsHardwareAccelerated)
    {
      return FromV256(Vector256.Divide(AsV256(left), AsV256(right)));
    }
    return new(left.x / right.x, left.y / right.y, left.z / right.z, left.w / right.w);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static T Dot(in Vec4<T> left, in Vec4<T> right)
  {
    if (Unsafe.SizeOf<T>() == 2 && Vector64.IsHardwareAccelerated)
    {
      return Vector64.Dot(AsV64(left), AsV64(right));
    }
    if (Unsafe.SizeOf<T>() == 4 && Vector128.IsHardwareAccelerated)
    {
      return Vector128.Dot(AsV128(left), AsV128(right));
    }
    if (Unsafe.SizeOf<T>() == 8 && Vector256.IsHardwareAccelerated)
    {
      return Vector256.Dot(AsV256(left), AsV256(right));
    }
    Vec4<T> prod = Multiply(left, right);
    return prod.x + prod.y + prod.z + prod.w;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec4<T> operator +(Vec4<T> left, Vec4<T> right) => Add(left, right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec4<T> operator -(Vec4<T> left, Vec4<T> right) => Subtract(left, right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec4<T> operator *(Vec4<T> left, Vec4<T> right) => Multiply(left, right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec4<T> operator /(Vec4<T> left, Vec4<T> right) => Divide(left, right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec4<T> Add(in Vec4<T> left, T right) => Add(left, new Vec4<T>(right));

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec4<T> Subtract(in Vec4<T> left, T right) => Subtract(left, new Vec4<T>(right));

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec4<T> Multiply(in Vec4<T> left, T right) => Multiply(left, new Vec4<T>(right));

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec4<T> Divide(in Vec4<T> left, T right) => Divide(left, new Vec4<T>(right));

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec4<T> operator +(Vec4<T> left, T right) => Add(left, right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec4<T> operator -(Vec4<T> left, T right) => Subtract(left, right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec4<T> operator *(Vec4<T> left, T right) => Multiply(left, right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec4<T> operator /(Vec4<T> left, T right) => Divide(left, right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static implicit operator (T X, T Y, T Z, T W)(in Vec4<T> vec) => (vec.X, vec.Y, vec.Z, vec.W);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static implicit operator Vec4<T>((T X, T Y, T Z, T W) tuple) => new Vec4<T>(tuple.X, tuple.Y, tuple.Z, tuple.W);

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static Vector64<T> AsV64(in Vec4<T> v) => Unsafe.As<Vec4<T>, Vector64<T>>(ref Unsafe.AsRef(in v));

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static Vec4<T> FromV64(in Vector64<T> v) => Unsafe.As<Vector64<T>, Vec4<T>>(ref Unsafe.AsRef(in v));

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static Vector128<T> AsV128(in Vec4<T> v) => Unsafe.As<Vec4<T>, Vector128<T>>(ref Unsafe.AsRef(in v));

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static Vec4<T> FromV128(in Vector128<T> v) => Unsafe.As<Vector128<T>, Vec4<T>>(ref Unsafe.AsRef(in v));

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static Vector256<T> AsV256(in Vec4<T> v) => Unsafe.As<Vec4<T>, Vector256<T>>(ref Unsafe.AsRef(in v));

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static Vec4<T> FromV256(in Vector256<T> v) => Unsafe.As<Vector256<T>, Vec4<T>>(ref Unsafe.AsRef(in v));
}
