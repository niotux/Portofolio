using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace QuantiX.Engine.Mathematics.Vectors;

[StructLayout(LayoutKind.Sequential)]
public partial record struct Vec3<T> : IVector<Vec3<T>>, IVectorOfScalar<Vec3<T>, T>, IVectorWithCrossProduct<Vec3<T>, Vec3<T>>, IVecXComponent<T>, IVecYComponent<T>, IVecZComponent<T>
{
  private T x;
  private T y;
  private T z;

  public Vec3(T val) : this(val, val, val) { }
  public Vec3(T x, T y, T z)
  {
    this.x = x;
    this.y = y;
    this.z = z;
  }

  public T X { get => this.x; set => this.x = value; }
  public T Y { get => this.y; set => this.y = value; }
  public T Z { get => this.z; set => this.z = value; }

  public T LengthSquared => Dot(this, this);

  public static readonly Vec3<T> Identity = new(T.One);
  public static readonly Vec3<T> Zero = new(T.Zero);
  public const int Dimensions = 3;

  static Vec3<T> IVector<Vec3<T>>.Identity => Identity;
  static Vec3<T> IVector<Vec3<T>>.Zero => Zero;
  static int IVector<Vec3<T>>.Dimensions => Dimensions;

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static T DistanceSquared(in Vec3<T> vec1, in Vec3<T> vec2) => Subtract(vec1, vec2).LengthSquared;

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec3<T> Add(in Vec3<T> left, in Vec3<T> right)
  {
    return new(left.x + right.x, left.y + right.y, left.z + right.z);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec3<T> Subtract(in Vec3<T> left, in Vec3<T> right)
  {
    return new(left.x - right.x, left.y - right.y, left.z - right.z);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec3<T> Multiply(in Vec3<T> left, in Vec3<T> right)
  {
    return new(left.x * right.x, left.y * right.y, left.z * right.z);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec3<T> Divide(in Vec3<T> left, in Vec3<T> right)
  {
    return new(left.x / right.x, left.y / right.y, left.z / right.z);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static T Dot(in Vec3<T> left, in Vec3<T> right)
  {
    Vec3<T> prod = Multiply(left, right);
    return prod.x + prod.y + prod.z;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec3<T> Cross(in Vec3<T> left, in Vec3<T> right)
  {
    return new(
      left.y * right.z - left.z * right.y,
      left.z * right.x - left.x * right.z,
      left.x * right.y - left.y * right.x
    );
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec3<T> operator +(Vec3<T> left, Vec3<T> right) => Add(left, right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec3<T> operator -(Vec3<T> left, Vec3<T> right) => Subtract(left, right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec3<T> operator *(Vec3<T> left, Vec3<T> right) => Multiply(left, right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec3<T> operator /(Vec3<T> left, Vec3<T> right) => Divide(left, right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec3<T> Add(in Vec3<T> left, T right) => Add(left, new Vec3<T>(right));

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec3<T> Subtract(in Vec3<T> left, T right) => Subtract(left, new Vec3<T>(right));

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec3<T> Multiply(in Vec3<T> left, T right) => Multiply(left, new Vec3<T>(right));

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec3<T> Divide(in Vec3<T> left, T right) => Divide(left, new Vec3<T>(right));

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec3<T> operator +(Vec3<T> left, T right) => Add(left, right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec3<T> operator -(Vec3<T> left, T right) => Subtract(left, right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec3<T> operator *(Vec3<T> left, T right) => Multiply(left, right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec3<T> operator /(Vec3<T> left, T right) => Divide(left, right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static implicit operator (T X, T Y, T Z)(in Vec3<T> vec) => (vec.X, vec.Y, vec.Z);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static implicit operator Vec3<T>((T X, T Y, T Z) tuple) => new Vec3<T>(tuple.X, tuple.Y, tuple.Z);
}
