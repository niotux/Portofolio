using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PrimordialPower.Mathematics.ConstArrays;
using System.Numerics;

namespace PrimordialPower.Mathematics.Vectors;


[StructLayout(LayoutKind.Sequential)]
public struct Vec4<T> : IVector<Vec4<T>, T>
where T : INumber<T>, new()
{
  private ConstArray4<T> array;



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
  public T W
  {
    get => array[3];
    set => array[3] = value;
  }



  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Vec4(T x, T y, T z, T w)
  {
    this.X = x;
    this.Y = y;
    this.Z = z;
    this.W = w;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Vec4(T value)
  {
    this.X = value;
    this.Y = value;
    this.Z = value;
    this.W = value;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Vec4(in Vec4<T> other)
  {
    this.X = other.X;
    this.Y = other.Y;
    this.Z = other.Z;
    this.W = other.W;
  }



  public T LengthSquared
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    get => X * X + Y * Y + Z * Z + W * W;
  }



  public static uint DimCount => 4;
  public uint Dimensions => DimCount;



  public static Vec4<T> Identity
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => new(T.One);
  }

  public static Vec4<T> Zero
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => new(T.Zero);
  }



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public T DistanceSquared(in Vec4<T> vector)
  => (this - vector).LengthSquared;



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Vec4<T> Neg()
  => new(-X, -Y, -Z, -W);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Vec4<T> Add(in Vec4<T> other)
  => new(X + other.X, Y + other.Y, Z + other.Z, W + other.W);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Vec4<T> Add(T scalar)
  => new(X + scalar, Y + scalar, Z + scalar, W + scalar);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Vec4<T> Subtract(in Vec4<T> other)
  => new(X - other.X, Y - other.Y, Z - other.Z, W - other.W);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Vec4<T> Subtract(T scalar)
  => new(X - scalar, Y - scalar, Z - scalar, W - scalar);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Vec4<T> Multiply(in Vec4<T> other)
  => new(X * other.X, Y * other.Y, Z * other.Z, W * other.W);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Vec4<T> Multiply(T scalar)
  => new(X * scalar, Y * scalar, Z * scalar, W * scalar);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Vec4<T> Divide(in Vec4<T> other)
  => new(X / other.X, Y / other.Y, Z / other.Z, W / other.W);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Vec4<T> Divide(T scalar)
  => new(X / scalar, Y / scalar, Z / scalar, W / scalar);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Vec4<T> Modulo(in Vec4<T> other)
  => new(X % other.X, Y % other.Y, Z % other.Z, W % other.W);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Vec4<T> Modulo(T scalar)
  => new(X % scalar, Y % scalar, Z % scalar, W % scalar);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public T Dot(in Vec4<T> other)
  => X * other.X + Y * other.Y + Z * other.Z + W * other.W;



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec4<T> operator -(in Vec4<T> right)
  => right.Neg();

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec4<T> operator +(in Vec4<T> left, in Vec4<T> right)
  => left.Add(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec4<T> operator +(in Vec4<T> left, T right)
  => left.Add(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec4<T> operator +(T left, in Vec4<T> right)
  => right.Add(left);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec4<T> operator -(in Vec4<T> left, in Vec4<T> right)
  => left.Subtract(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec4<T> operator -(in Vec4<T> left, T right)
  => left.Subtract(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec4<T> operator *(in Vec4<T> left, in Vec4<T> right)
  => left.Multiply(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec4<T> operator *(in Vec4<T> left, T right)
  => left.Multiply(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec4<T> operator *(T left, in Vec4<T> right)
  => right.Multiply(left);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec4<T> operator %(in Vec4<T> left, in Vec4<T> right)
  => left.Modulo(right);

  public static Vec4<T> operator %(in Vec4<T> left, T right)
  => left.Modulo(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec4<T> operator /(in Vec4<T> left, T right)
  => left.Divide(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec4<T> operator /(in Vec4<T> left, in Vec4<T> right)
  => left.Divide(right);



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static implicit operator (T X, T Y, T Z, T W)(in Vec4<T> vector)
  => (vector.X, vector.Y, vector.Z, vector.W);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static implicit operator Vec4<T>(in (T, T, T, T) tuple)
  => new(tuple.Item1, tuple.Item2, tuple.Item3, tuple.Item4);



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



  public override string ToString() => Vector.ToString<Vec4<T>, T>(this);
}
