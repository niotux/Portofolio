using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PrimordialPower.Mathematics.ConstArrays;
using System.Numerics;

namespace PrimordialPower.Mathematics.Vectors;


[StructLayout(LayoutKind.Sequential)]
public struct Vec2<T> :
  IVector<Vec2<T>, T>,
  IVectorHasCross<Vec2<T>, Vec3<T>>
where T : INumber<T>, new()
{
  private ConstArray2<T> array;


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



  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Vec2(T x, T y)
  {
    this.X = x;
    this.Y = y;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Vec2(T value)
  {
    this.X = value;
    this.Y = value;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Vec2(in Vec2<T> other)
  {
    this.X = other.X;
    this.Y = other.Y;
  }



  public T LengthSquared
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    get => X * X + Y * Y;
  }



  public static uint DimCount => 2;
  public uint Dimensions => DimCount;



  public static Vec2<T> Identity
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => new(T.One);
  }

  public static Vec2<T> Zero
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => new(T.Zero);
  }



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public T DistanceSquared(in Vec2<T> vector)
  => (this - vector).LengthSquared;



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Vec2<T> Neg()
  => new(-X, -Y);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Vec2<T> Add(in Vec2<T> other)
  => new(X + other.X, Y + other.Y);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Vec2<T> Add(T scalar)
  => new(X + scalar, Y + scalar);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Vec2<T> Subtract(in Vec2<T> other)
  => new(X - other.X, Y - other.Y);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Vec2<T> Subtract(T scalar)
  => new(X - scalar, Y - scalar);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Vec2<T> Multiply(in Vec2<T> other)
  => new(X * other.X, Y * other.Y);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Vec2<T> Multiply(T scalar)
  => new(X * scalar, Y * scalar);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Vec2<T> Divide(in Vec2<T> other)
  => new(X / other.X, Y / other.Y);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Vec2<T> Divide(T scalar)
  => new(X / scalar, Y / scalar);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Vec2<T> Modulo(in Vec2<T> other)
  => new(X % other.X, Y % other.Y);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Vec2<T> Modulo(T scalar)
  => new(X % scalar, Y % scalar);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public T Dot(in Vec2<T> other)
  => X * other.X + Y * other.Y;

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Vec3<T> Cross(in Vec2<T> other)
  => new(
    T.Zero,
    T.Zero,
    X * other.Y - Y * other.X
  );



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec2<T> operator -(in Vec2<T> right)
  => right.Neg();

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec2<T> operator +(in Vec2<T> left, in Vec2<T> right)
  => left.Add(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec2<T> operator +(in Vec2<T> left, T right)
  => left.Add(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec2<T> operator +(T left, in Vec2<T> right)
  => right.Add(left);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec2<T> operator -(in Vec2<T> left, in Vec2<T> right)
  => left.Subtract(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec2<T> operator -(in Vec2<T> left, T right)
  => left.Subtract(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec2<T> operator *(in Vec2<T> left, in Vec2<T> right)
  => left.Multiply(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec2<T> operator *(in Vec2<T> left, T right)
  => left.Multiply(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec2<T> operator *(T left, in Vec2<T> right)
  => right.Multiply(left);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec2<T> operator %(in Vec2<T> left, in Vec2<T> right)
  => left.Modulo(right);

  public static Vec2<T> operator %(in Vec2<T> left, T right)
  => left.Modulo(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec2<T> operator /(in Vec2<T> left, T right)
  => left.Divide(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec2<T> operator /(in Vec2<T> left, in Vec2<T> right)
  => left.Divide(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec3<T> operator ^(in Vec2<T> left, in Vec2<T> right)
  => left.Cross(right);



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static implicit operator (T X, T Y)(in Vec2<T> vector)
  => (vector.X, vector.Y);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static implicit operator Vec2<T>(in (T, T) tuple)
  => new Vec2<T>(tuple.Item1, tuple.Item2);



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



  public override string ToString() => Vector.ToString<Vec2<T>, T>(this);
}