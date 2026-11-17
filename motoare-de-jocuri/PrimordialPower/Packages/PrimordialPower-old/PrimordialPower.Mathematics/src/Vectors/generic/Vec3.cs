using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PrimordialPower.Mathematics.ConstArrays;
using System.Numerics;

namespace PrimordialPower.Mathematics.Vectors;


[StructLayout(LayoutKind.Sequential)]
public struct Vec3<T> :
  IVector<Vec3<T>, T>,
  IVectorHasCross<Vec3<T>, Vec3<T>>
where T : INumber<T>, new()
{
  private ConstArray3<T> array;



  public T X
  {
    get => array[0];
    set => array[0] = value;
  }
  public T Y
  {
    get => array[1];
    set => array[1] = value;
  }
  public T Z
  {
    get => array[2];
    set => array[2] = value;
  }



  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Vec3(T x, T y, T z)
  {
    this.X = x;
    this.Y = y;
    this.Z = z;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Vec3(T value)
  {
    this.X = value;
    this.Y = value;
    this.Z = value;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Vec3(in Vec3<T> other)
  {
    this.X = other.X;
    this.Y = other.Y;
    this.Z = other.Z;
  }



  public T LengthSquared
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    get => X * X + Y * Y + Z * Z;
  }



  public static uint DimCount => 3;
  public uint Dimensions => DimCount;



  public static Vec3<T> Identity
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => new Vec3<T>(T.One);
  }

  public static Vec3<T> Zero
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => new Vec3<T>(T.Zero);
  }



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public T DistanceSquared(in Vec3<T> vector)
  => (this - vector).LengthSquared;



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Vec3<T> Neg()
  => new(-X, -Y, -Z);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Vec3<T> Add(in Vec3<T> other)
  => new(X + other.X, Y + other.Y, Z + other.Z);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Vec3<T> Add(T scalar)
  => new(X + scalar, Y + scalar, Z + scalar);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Vec3<T> Subtract(in Vec3<T> other)
  => new(X - other.X, Y - other.Y, Z - other.Z);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Vec3<T> Subtract(T scalar)
  => new(X - scalar, Y - scalar, Z - scalar);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Vec3<T> Multiply(in Vec3<T> other)
  => new(X * other.X, Y * other.Y, Z * other.Z);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Vec3<T> Multiply(T scalar)
  => new(X * scalar, Y * scalar, Z * scalar);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Vec3<T> Divide(in Vec3<T> other)
  => new(X / other.X, Y / other.Y, Z / other.Z);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Vec3<T> Divide(T scalar)
  => new(X / scalar, Y / scalar, Z / scalar);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Vec3<T> Modulo(in Vec3<T> other)
  => new(X % other.X, Y % other.Y, Z % other.Z);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Vec3<T> Modulo(T scalar)
  => new(X % scalar, Y % scalar, Z % scalar);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public T Dot(in Vec3<T> other)
  => X * other.X + Y * other.Y + Z * other.Z;

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Vec3<T> Cross(in Vec3<T> other)
  => new(
    Y * other.Z - Z * other.Y,
    Z * other.X - X * other.Z,
    X * other.Y - Y * other.X
  );



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec3<T> operator -(in Vec3<T> right)
  => right.Neg();

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec3<T> operator +(in Vec3<T> left, in Vec3<T> right)
  => left.Add(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec3<T> operator +(in Vec3<T> left, T right)
  => left.Add(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec3<T> operator +(T left, in Vec3<T> right)
  => right.Add(left);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec3<T> operator -(in Vec3<T> left, in Vec3<T> right)
  => left.Subtract(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec3<T> operator -(in Vec3<T> left, T right)
  => left.Subtract(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec3<T> operator *(in Vec3<T> left, in Vec3<T> right)
  => left.Multiply(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec3<T> operator *(in Vec3<T> left, T right)
  => left.Multiply(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec3<T> operator *(T left, in Vec3<T> right)
  => right.Multiply(left);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec3<T> operator %(in Vec3<T> left, in Vec3<T> right)
  => left.Modulo(right);

  public static Vec3<T> operator %(in Vec3<T> left, T right)
  => left.Modulo(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec3<T> operator /(in Vec3<T> left, T right)
  => left.Divide(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec3<T> operator /(in Vec3<T> left, in Vec3<T> right)
  => left.Divide(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec3<T> operator ^(in Vec3<T> left, in Vec3<T> right)
  => left.Cross(right);



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static implicit operator (T X, T Y, T Z)(in Vec3<T> vector)
  => (vector.X, vector.Y, vector.Z);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static implicit operator Vec3<T>(in (T, T, T) tuple)
  => new Vec3<T>(tuple.Item1, tuple.Item2, tuple.Item3);



  public T this[int index]
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    get => this.array[index];
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    set => this.array[index] = value;
  }

  public T this[uint index]
  {
    get => this[(int)index];
    set => this[(int)index] = value;
  }



  public override string ToString() => Vector.ToString<Vec3<T>, T>(this);
}
