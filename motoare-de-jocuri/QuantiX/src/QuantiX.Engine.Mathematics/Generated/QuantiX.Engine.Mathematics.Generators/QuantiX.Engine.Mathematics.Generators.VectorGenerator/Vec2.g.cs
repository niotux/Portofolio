using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace QuantiX.Engine.Mathematics.Vectors;

[StructLayout(LayoutKind.Sequential)]
public partial record struct Vec2<T> : IVector<Vec2<T>>, IVectorOfScalar<Vec2<T>, T>, IVectorWithCrossProduct<Vec2<T>, Vec3<T>>, IVecXComponent<T>, IVecYComponent<T>
{
  private T x;
  private T y;

  public Vec2(T val) : this(val, val) { }
  public Vec2(T x, T y)
  {
    this.x = x;
    this.y = y;
  }

  public T X { get => this.x; set => this.x = value; }
  public T Y { get => this.y; set => this.y = value; }

  public T LengthSquared => Dot(this, this);

  public static readonly Vec2<T> Identity = new(T.One);
  public static readonly Vec2<T> Zero = new(T.Zero);
  public const int Dimensions = 2;

  static Vec2<T> IVector<Vec2<T>>.Identity => Identity;
  static Vec2<T> IVector<Vec2<T>>.Zero => Zero;
  static int IVector<Vec2<T>>.Dimensions => Dimensions;

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static T DistanceSquared(in Vec2<T> vec1, in Vec2<T> vec2) => Subtract(vec1, vec2).LengthSquared;

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec2<T> Add(in Vec2<T> left, in Vec2<T> right)
  {
    if (Unsafe.SizeOf<T>() == 4 && Vector64.IsHardwareAccelerated)
    {
      return FromV64(Vector64.Add(AsV64(left), AsV64(right)));
    }
    if (Unsafe.SizeOf<T>() == 8 && Vector128.IsHardwareAccelerated)
    {
      return FromV128(Vector128.Add(AsV128(left), AsV128(right)));
    }
    return new(left.x + right.x, left.y + right.y);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec2<T> Subtract(in Vec2<T> left, in Vec2<T> right)
  {
    if (Unsafe.SizeOf<T>() == 4 && Vector64.IsHardwareAccelerated)
    {
      return FromV64(Vector64.Subtract(AsV64(left), AsV64(right)));
    }
    if (Unsafe.SizeOf<T>() == 8 && Vector128.IsHardwareAccelerated)
    {
      return FromV128(Vector128.Subtract(AsV128(left), AsV128(right)));
    }
    return new(left.x - right.x, left.y - right.y);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec2<T> Multiply(in Vec2<T> left, in Vec2<T> right)
  {
    if (Unsafe.SizeOf<T>() == 4 && Vector64.IsHardwareAccelerated)
    {
      return FromV64(Vector64.Multiply(AsV64(left), AsV64(right)));
    }
    if (Unsafe.SizeOf<T>() == 8 && Vector128.IsHardwareAccelerated)
    {
      return FromV128(Vector128.Multiply(AsV128(left), AsV128(right)));
    }
    return new(left.x * right.x, left.y * right.y);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec2<T> Divide(in Vec2<T> left, in Vec2<T> right)
  {
    if (Unsafe.SizeOf<T>() == 4 && Vector64.IsHardwareAccelerated)
    {
      return FromV64(Vector64.Divide(AsV64(left), AsV64(right)));
    }
    if (Unsafe.SizeOf<T>() == 8 && Vector128.IsHardwareAccelerated)
    {
      return FromV128(Vector128.Divide(AsV128(left), AsV128(right)));
    }
    return new(left.x / right.x, left.y / right.y);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static T Dot(in Vec2<T> left, in Vec2<T> right)
  {
    if (Unsafe.SizeOf<T>() == 4 && Vector64.IsHardwareAccelerated)
    {
      return Vector64.Dot(AsV64(left), AsV64(right));
    }
    if (Unsafe.SizeOf<T>() == 8 && Vector128.IsHardwareAccelerated)
    {
      return Vector128.Dot(AsV128(left), AsV128(right));
    }
    Vec2<T> prod = Multiply(left, right);
    return prod.x + prod.y;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec3<T> Cross(in Vec2<T> left, in Vec2<T> right)
  {
    return new(T.Zero, T.Zero, left.x * right.y - left.y * right.x);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec2<T> operator +(Vec2<T> left, Vec2<T> right) => Add(left, right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec2<T> operator -(Vec2<T> left, Vec2<T> right) => Subtract(left, right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec2<T> operator *(Vec2<T> left, Vec2<T> right) => Multiply(left, right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec2<T> operator /(Vec2<T> left, Vec2<T> right) => Divide(left, right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec2<T> Add(in Vec2<T> left, T right) => Add(left, new Vec2<T>(right));

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec2<T> Subtract(in Vec2<T> left, T right) => Subtract(left, new Vec2<T>(right));

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec2<T> Multiply(in Vec2<T> left, T right) => Multiply(left, new Vec2<T>(right));

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec2<T> Divide(in Vec2<T> left, T right) => Divide(left, new Vec2<T>(right));

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec2<T> operator +(Vec2<T> left, T right) => Add(left, right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec2<T> operator -(Vec2<T> left, T right) => Subtract(left, right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec2<T> operator *(Vec2<T> left, T right) => Multiply(left, right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec2<T> operator /(Vec2<T> left, T right) => Divide(left, right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static implicit operator (T X, T Y)(in Vec2<T> vec) => (vec.X, vec.Y);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static implicit operator Vec2<T>((T X, T Y) tuple) => new Vec2<T>(tuple.X, tuple.Y);

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static Vector64<T> AsV64(in Vec2<T> v) => Unsafe.As<Vec2<T>, Vector64<T>>(ref Unsafe.AsRef(in v));

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static Vec2<T> FromV64(in Vector64<T> v) => Unsafe.As<Vector64<T>, Vec2<T>>(ref Unsafe.AsRef(in v));

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static Vector128<T> AsV128(in Vec2<T> v) => Unsafe.As<Vec2<T>, Vector128<T>>(ref Unsafe.AsRef(in v));

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static Vec2<T> FromV128(in Vector128<T> v) => Unsafe.As<Vector128<T>, Vec2<T>>(ref Unsafe.AsRef(in v));
}
