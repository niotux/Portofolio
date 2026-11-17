using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PrimordialPower.Mathematics.Matrices;
using PrimordialPower.Mathematics.ConstArrays;

namespace PrimordialPower.Mathematics.Vectors;

using ThisVector = Vec5D;
using ThisValue = double;

[StructLayout(LayoutKind.Explicit)]
public struct Vec5D :
  IVectorFloatingIeee754<ThisVector, ThisValue>,
  IVectorConvertible
{
  [FieldOffset(0)]
  private ConstArray5<ThisValue> array;



  [FieldOffset(sizeof(ThisValue) * 0)]
  public ThisValue X;
  [FieldOffset(sizeof(ThisValue) * 1)]
  public ThisValue Y;
  [FieldOffset(sizeof(ThisValue) * 2)]
  public ThisValue Z;
  [FieldOffset(sizeof(ThisValue) * 3)]
  public ThisValue W;
  [FieldOffset(sizeof(ThisValue) * 4)]
  public ThisValue V;



  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Vec5D(ThisValue x, ThisValue y, ThisValue z, ThisValue w, ThisValue v)
  {
    this.X = x;
    this.Y = y;
    this.Z = z;
    this.W = w;
    this.V = v;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Vec5D(ThisValue value)
  {
    this.X = value;
    this.Y = value;
    this.Z = value;
    this.W = value;
    this.V = value;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Vec5D(in ThisVector other)
  {
    this.X = other.X;
    this.Y = other.Y;
    this.Z = other.Z;
    this.W = other.W;
    this.V = other.V;
  }



  public ThisValue Length
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    get => ThisValue.Sqrt(this.LengthSquared);

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    set => this = this.Normalized * value;
  }

  public ThisValue LengthFast
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    get => MathExt.FastSqrt(this.LengthSquared, Vector.FastSqrtNewtonRaphsonIterations);

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    set => this = this.NormalizedFast * value;
  }

  public ThisValue LengthSquared
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    get => X * X + Y * Y + Z * Z + W * W + V * V;
  }



  public ThisVector Normalized
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    get
    {
      ThisValue length = this.Length;
      return new(X / length, Y / length, Z / length, W / length, V / length);
    }
  }

  public ThisVector NormalizedFast
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    get
    {
      ThisValue exp = MathExt.FastInvSqrt(this.LengthSquared, Vector.FastSqrtNewtonRaphsonIterations);
      return new(X * exp, Y * exp, Z * exp, W * exp, V * exp);
    }
  }



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public void Normalize()
  {
    this = this.Normalized;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public void NormalizeFast()
  {
    this = this.NormalizedFast;
  }



  public static uint DimCount => 5;
  public uint Dimensions => DimCount;



  public static ThisVector Identity
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => new(1F);
  }

  public static ThisVector Zero
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => new(0F);
  }



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisValue Distance(in ThisVector vector)
  => (this - vector).Length;

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisValue DistanceSquared(in ThisVector vector)
  => (this - vector).LengthSquared;

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisValue DistanceFast(in ThisVector vector)
  => (this - vector).LengthFast;



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisVector Neg()
  => new(-X, -Y, -Z, -W, -V);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisVector Add(in ThisVector other)
  => new(X + other.X, Y + other.Y, Z + other.Z, W + other.W, V + other.V);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisVector Add(ThisValue scalar)
  => new(X + scalar, Y + scalar, Z + scalar, W + scalar, V + scalar);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisVector Subtract(in ThisVector other)
  => new(X - other.X, Y - other.Y, Z - other.Z, W - other.W, V - other.V);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisVector Subtract(ThisValue scalar)
  => new(X - scalar, Y - scalar, Z - scalar, W - scalar, V - scalar);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisVector Multiply(in ThisVector other)
  => new(X * other.X, Y * other.Y, Z * other.Z, W * other.W, V * other.V);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisVector Multiply(ThisValue scalar)
  => new(X * scalar, Y * scalar, Z * scalar, W * scalar, V * scalar);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisVector Divide(in ThisVector other)
  => new(X / other.X, Y / other.Y, Z / other.Z, W / other.W, V / other.V);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisVector Divide(ThisValue scalar)
  => new(X / scalar, Y / scalar, Z / scalar, W / scalar, V / scalar);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisVector Modulo(in ThisVector other)
  => new(X % other.X, Y % other.Y, Z % other.Z, W % other.W, V % other.V);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisVector Modulo(ThisValue scalar)
  => new(X % scalar, Y % scalar, Z % scalar, W % scalar, V % scalar);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisValue Dot(in ThisVector other)
  => X * other.X + Y * other.Y + Z * other.Z + W * other.W + V * other.V;



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static ThisVector operator -(in ThisVector right)
  => right.Neg();

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static ThisVector operator +(in ThisVector left, in ThisVector right)
  => left.Add(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static ThisVector operator +(in ThisVector left, ThisValue right)
  => left.Add(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static ThisVector operator +(ThisValue left, in ThisVector right)
  => right.Add(left);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static ThisVector operator -(in ThisVector left, in ThisVector right)
  => left.Subtract(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static ThisVector operator -(in ThisVector left, ThisValue right)
  => left.Subtract(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static ThisVector operator *(in ThisVector left, in ThisVector right)
  => left.Multiply(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static ThisVector operator *(in ThisVector left, ThisValue right)
  => left.Multiply(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static ThisVector operator *(ThisValue left, in ThisVector right)
  => right.Multiply(left);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static ThisVector operator %(in ThisVector left, in ThisVector right)
  => left.Modulo(right);

  public static ThisVector operator %(in ThisVector left, ThisValue right)
  => left.Modulo(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static ThisVector operator /(in ThisVector left, ThisValue right)
  => left.Divide(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static ThisVector operator /(in ThisVector left, in ThisVector right)
  => left.Divide(right);

  public static ThisVector operator ~(in ThisVector right)
  => right.Normalized;



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static implicit operator Vec5<ThisValue>(in ThisVector vector)
  => new(vector.X, vector.Y, vector.Z, vector.W, vector.V);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static implicit operator ThisVector(in Vec5<ThisValue> vector)
  => new(vector.X, vector.Y, vector.Z, vector.W, vector.V);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static implicit operator (ThisValue X, ThisValue Y, ThisValue Z, ThisValue W, ThisValue V)(in ThisVector vector)
  => (vector.X, vector.Y, vector.Z, vector.W, vector.V);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static implicit operator ThisVector(in (ThisValue, ThisValue, ThisValue, ThisValue, ThisValue) tuple)
  => new(tuple.Item1, tuple.Item2, tuple.Item3, tuple.Item4, tuple.Item5);



  public ThisValue this[int index]
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    get => this.array[index];
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    set => this.array[index] = value;
  }

  public ThisValue this[uint index]
  {
    get => this[(int)index];
    set => this[(int)index] = value;
  }



  public Vec2F ToVec2F() => new((float)this.X, (float)this.Y);
  public Vec3F ToVec3F() => new((float)this.X, (float)this.Y, (float)this.Z);
  public Vec4F ToVec4F() => new((float)this.X, (float)this.Y, (float)this.Z, (float)this.W);
  public Vec5F ToVec5F() => new((float)this.X, (float)this.Y, (float)this.Z, (float)this.W, (float)this.V);
  public Vec2D ToVec2D() => new(this.X, this.Y);
  public Vec3D ToVec3D() => new(this.X, this.Y, this.Z);
  public Vec4D ToVec4D() => new(this.X, this.Y, this.Z, this.W);
  public Vec5D ToVec5D() => new(this.X, this.Y, this.Z, this.W, this.V);
  public Vec2I ToVec2I() => new((int)this.X, (int)this.Y);
  public Vec3I ToVec3I() => new((int)this.X, (int)this.Y, (int)this.Z);
  public Vec4I ToVec4I() => new((int)this.X, (int)this.Y, (int)this.Z, (int)this.W);
  public Vec5I ToVec5I() => new((int)this.X, (int)this.Y, (int)this.Z, (int)this.W, (int)this.V);



  public override string ToString() => Vector.ToString<ThisVector, ThisValue>(this);
}
