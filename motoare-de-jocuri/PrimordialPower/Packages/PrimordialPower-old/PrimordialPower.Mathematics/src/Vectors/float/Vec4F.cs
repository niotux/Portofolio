using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PrimordialPower.Mathematics.Matrices;
using PrimordialPower.Mathematics.ConstArrays;

namespace PrimordialPower.Mathematics.Vectors;

using ThisVector = Vec4F;
using ThisValue = float;

[StructLayout(LayoutKind.Explicit)]
public struct Vec4F :
  IVectorFloatingIeee754<ThisVector, ThisValue>,
  IVectorConvertible
{
  [FieldOffset(0)]
  private ConstArray4<ThisValue> array;



  [FieldOffset(sizeof(ThisValue) * 0)]
  public ThisValue X;
  [FieldOffset(sizeof(ThisValue) * 1)]
  public ThisValue Y;
  [FieldOffset(sizeof(ThisValue) * 2)]
  public ThisValue Z;
  [FieldOffset(sizeof(ThisValue) * 3)]
  public ThisValue W;



  [FieldOffset(sizeof(ThisValue) * 0)]
  public ThisValue TextureS;
  [FieldOffset(sizeof(ThisValue) * 1)]
  public ThisValue TextureT;
  [FieldOffset(sizeof(ThisValue) * 2)]
  public ThisValue TextureR;
  [FieldOffset(sizeof(ThisValue) * 3)]
  public ThisValue TextureQ;



  [FieldOffset(sizeof(ThisValue) * 0)]
  public ThisValue Red;
  [FieldOffset(sizeof(ThisValue) * 1)]
  public ThisValue Green;
  [FieldOffset(sizeof(ThisValue) * 2)]
  public ThisValue Blue;
  [FieldOffset(sizeof(ThisValue) * 3)]
  public ThisValue Alpha;



  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Vec4F(ThisValue x, ThisValue y, ThisValue z, ThisValue w)
  {
    this.X = x;
    this.Y = y;
    this.Z = z;
    this.W = w;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Vec4F(ThisValue value)
  {
    this.X = value;
    this.Y = value;
    this.Z = value;
    this.W = value;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Vec4F(in ThisVector other)
  {
    this.X = other.X;
    this.Y = other.Y;
    this.Z = other.Z;
    this.W = other.W;
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
    get => X * X + Y * Y + Z * Z + W * W;
  }



  public ThisVector Normalized
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    get
    {
      ThisValue length = this.Length;
      return new(X / length, Y / length, Z / length, W / length);
    }
  }

  public ThisVector NormalizedFast
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    get
    {
      ThisValue exp = MathExt.FastInvSqrt(this.LengthSquared, Vector.FastSqrtNewtonRaphsonIterations);
      return new(X * exp, Y * exp, Z * exp, W * exp);
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



  public static uint DimCount => 4;
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
  => new(-X, -Y, -Z, -W);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisVector Add(in ThisVector other)
  => new(X + other.X, Y + other.Y, Z + other.Z, W + other.W);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisVector Add(ThisValue scalar)
  => new(X + scalar, Y + scalar, Z + scalar, W + scalar);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisVector Subtract(in ThisVector other)
  => new(X - other.X, Y - other.Y, Z - other.Z, W - other.W);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisVector Subtract(ThisValue scalar)
  => new(X - scalar, Y - scalar, Z - scalar, W - scalar);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisVector Multiply(in ThisVector other)
  => new(X * other.X, Y * other.Y, Z * other.Z, W * other.W);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisVector Multiply(ThisValue scalar)
  => new(X * scalar, Y * scalar, Z * scalar, W * scalar);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisVector Divide(in ThisVector other)
  => new(X / other.X, Y / other.Y, Z / other.Z, W / other.W);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisVector Divide(ThisValue scalar)
  => new(X / scalar, Y / scalar, Z / scalar, W / scalar);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisVector Modulo(in ThisVector other)
  => new(X % other.X, Y % other.Y, Z % other.Z, W % other.W);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisVector Modulo(ThisValue scalar)
  => new(X % scalar, Y % scalar, Z % scalar, W % scalar);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisValue Dot(in ThisVector other)
  => X * other.X + Y * other.Y + Z * other.Z + W * other.W;

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisVector Cross(in ThisVector other)
  => new(
    Y * other.Z - Z * other.Y,
    Z * other.X - X * other.Z,
    X * other.Y - Y * other.X,
    0F
  );



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

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static ThisVector operator ^(in ThisVector left, in ThisVector right)
  => left.Cross(right);

  public static ThisVector operator ~(in ThisVector right)
  => right.Normalized;



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static implicit operator Vec4<ThisValue>(in ThisVector vector)
  => new(vector.X, vector.Y, vector.Z, vector.W);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static implicit operator ThisVector(in Vec4<ThisValue> vector)
  => new(vector.X, vector.Y, vector.Z, vector.W);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static implicit operator (ThisValue X, ThisValue Y, ThisValue Z, ThisValue W)(in ThisVector vector)
  => (vector.X, vector.Y, vector.Z, vector.W);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static implicit operator ThisVector(in (ThisValue, ThisValue, ThisValue, ThisValue) tuple)
  => new(tuple.Item1, tuple.Item2, tuple.Item3, tuple.Item4);



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
  public Vec5F ToVec5F() => new((float)this.X, (float)this.Y, (float)this.Z, (float)this.W, 0F);
  public Vec2D ToVec2D() => new(this.X, this.Y);
  public Vec3D ToVec3D() => new(this.X, this.Y, this.Z);
  public Vec4D ToVec4D() => new(this.X, this.Y, this.Z, this.W);
  public Vec5D ToVec5D() => new(this.X, this.Y, this.Z, this.W, 0D);
  public Vec2I ToVec2I() => new((int)this.X, (int)this.Y);
  public Vec3I ToVec3I() => new((int)this.X, (int)this.Y, (int)this.Z);
  public Vec4I ToVec4I() => new((int)this.X, (int)this.Y, (int)this.Z, (int)this.W);
  public Vec5I ToVec5I() => new((int)this.X, (int)this.Y, (int)this.Z, (int)this.W, 0);



  public override string ToString() => Vector.ToString<ThisVector, ThisValue>(this);
}
