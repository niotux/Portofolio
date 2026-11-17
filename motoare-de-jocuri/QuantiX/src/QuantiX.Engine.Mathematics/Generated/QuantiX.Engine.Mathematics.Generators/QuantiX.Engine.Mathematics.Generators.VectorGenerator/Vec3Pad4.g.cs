using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace QuantiX.Engine.Mathematics.Vectors;

[StructLayout(LayoutKind.Sequential)]
public partial record struct Vec3Pad4<T> : IVector<Vec3Pad4<T>>, IVectorOfScalar<Vec3Pad4<T>, T>, IVectorWithCrossProduct<Vec3Pad4<T>, Vec3Pad4<T>>, IVecXComponent<T>, IVecYComponent<T>, IVecZComponent<T>
{
  private T x;
  private T y;
  private T z;
  private readonly T __w;

  public Vec3Pad4(T val) : this(val, val, val) { }
  public Vec3Pad4(T x, T y, T z)
  {
    this.x = x;
    this.y = y;
    this.z = z;
    __w = T.Zero;
  }

  public T X { get => this.x; set => this.x = value; }
  public T Y { get => this.y; set => this.y = value; }
  public T Z { get => this.z; set => this.z = value; }

  public T LengthSquared => Dot(this, this);

  public static readonly Vec3Pad4<T> Identity = new(T.One);
  public static readonly Vec3Pad4<T> Zero = new(T.Zero);
  public const int Dimensions = 3;

  static Vec3Pad4<T> IVector<Vec3Pad4<T>>.Identity => Identity;
  static Vec3Pad4<T> IVector<Vec3Pad4<T>>.Zero => Zero;
  static int IVector<Vec3Pad4<T>>.Dimensions => Dimensions;

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static T DistanceSquared(in Vec3Pad4<T> vec1, in Vec3Pad4<T> vec2) => Subtract(vec1, vec2).LengthSquared;

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec3Pad4<T> Add(in Vec3Pad4<T> left, in Vec3Pad4<T> right)
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
    return new(left.x + right.x, left.y + right.y, left.z + right.z);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec3Pad4<T> Subtract(in Vec3Pad4<T> left, in Vec3Pad4<T> right)
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
    return new(left.x - right.x, left.y - right.y, left.z - right.z);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec3Pad4<T> Multiply(in Vec3Pad4<T> left, in Vec3Pad4<T> right)
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
    return new(left.x * right.x, left.y * right.y, left.z * right.z);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec3Pad4<T> Divide(in Vec3Pad4<T> left, in Vec3Pad4<T> right)
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
    return new(left.x / right.x, left.y / right.y, left.z / right.z);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static T Dot(in Vec3Pad4<T> left, in Vec3Pad4<T> right)
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
    Vec3Pad4<T> prod = Multiply(left, right);
    return prod.x + prod.y + prod.z;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec3Pad4<T> Cross(in Vec3Pad4<T> left, in Vec3Pad4<T> right)
  {
    // float: 2x shufps + 2x mulps + 1x subps  (SSE)
    if (typeof(T) == typeof(float) && Sse.IsSupported)
    {
      var a = Unsafe.As<Vec3Pad4<T>, Vector128<float>>(ref Unsafe.AsRef(in left));
      var b = Unsafe.As<Vec3Pad4<T>, Vector128<float>>(ref Unsafe.AsRef(in right));
      var aYZX = Sse.Shuffle(a, a, 0xC9);
      var bZXY = Sse.Shuffle(b, b, 0xD2);
      var aZXY = Sse.Shuffle(a, a, 0xD2);
      var bYZX = Sse.Shuffle(b, b, 0xC9);
      var r = Sse.Subtract(Sse.Multiply(aYZX, bZXY), Sse.Multiply(aZXY, bYZX));
      return Unsafe.As<Vector128<float>, Vec3Pad4<T>>(ref r);
    }

    // double: 2x vpermq + 2x vmulpd + 1x vsubpd  (AVX2)
    if (typeof(T) == typeof(double) && Avx2.IsSupported)
    {
      var a = Unsafe.As<Vec3Pad4<T>, Vector256<double>>(ref Unsafe.AsRef(in left));
      var b = Unsafe.As<Vec3Pad4<T>, Vector256<double>>(ref Unsafe.AsRef(in right));
      var aYZX = Avx2.Permute4x64(a, 0xC9);
      var bZXY = Avx2.Permute4x64(b, 0xD2);
      var aZXY = Avx2.Permute4x64(a, 0xD2);
      var bYZX = Avx2.Permute4x64(b, 0xC9);
      var r = Avx.Subtract(Avx.Multiply(aYZX, bZXY), Avx.Multiply(aZXY, bYZX));
      return Unsafe.As<Vector256<double>, Vec3Pad4<T>>(ref r);
    }

    // Half: byte-level shuffle in Vector64 (ARM NEON)
    if (typeof(T) == typeof(Half) && Vector64.IsHardwareAccelerated)
    {
      var aBytes = AsV64(left).AsByte();
      var bBytes = AsV64(right).AsByte();
      var yzxIdx = Vector64.Create((byte)2, 3, 4, 5, 0, 1, 6, 7);
      var zxyIdx = Vector64.Create((byte)4, 5, 0, 1, 2, 3, 6, 7);
      var aYZXb = Vector64.Shuffle(aBytes, yzxIdx);
      var bZXYb = Vector64.Shuffle(bBytes, zxyIdx);
      var aZXYb = Vector64.Shuffle(aBytes, zxyIdx);
      var bYZXb = Vector64.Shuffle(bBytes, yzxIdx);
      var aYZX = Unsafe.As<Vector64<byte>, Vector64<T>>(ref aYZXb);
      var bZXY = Unsafe.As<Vector64<byte>, Vector64<T>>(ref bZXYb);
      var aZXY = Unsafe.As<Vector64<byte>, Vector64<T>>(ref aZXYb);
      var bYZX = Unsafe.As<Vector64<byte>, Vector64<T>>(ref bYZXb);
      return FromV64(Vector64.Subtract(Vector64.Multiply(aYZX, bZXY), Vector64.Multiply(aZXY, bYZX)));
    }

    return new(
      left.y * right.z - left.z * right.y,
      left.z * right.x - left.x * right.z,
      left.x * right.y - left.y * right.x
    );
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec3Pad4<T> operator +(Vec3Pad4<T> left, Vec3Pad4<T> right) => Add(left, right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec3Pad4<T> operator -(Vec3Pad4<T> left, Vec3Pad4<T> right) => Subtract(left, right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec3Pad4<T> operator *(Vec3Pad4<T> left, Vec3Pad4<T> right) => Multiply(left, right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec3Pad4<T> operator /(Vec3Pad4<T> left, Vec3Pad4<T> right) => Divide(left, right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec3Pad4<T> Add(in Vec3Pad4<T> left, T right) => Add(left, new Vec3Pad4<T>(right));

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec3Pad4<T> Subtract(in Vec3Pad4<T> left, T right) => Subtract(left, new Vec3Pad4<T>(right));

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec3Pad4<T> Multiply(in Vec3Pad4<T> left, T right) => Multiply(left, new Vec3Pad4<T>(right));

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec3Pad4<T> Divide(in Vec3Pad4<T> left, T right) => Divide(left, new Vec3Pad4<T>(right));

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec3Pad4<T> operator +(Vec3Pad4<T> left, T right) => Add(left, right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec3Pad4<T> operator -(Vec3Pad4<T> left, T right) => Subtract(left, right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec3Pad4<T> operator *(Vec3Pad4<T> left, T right) => Multiply(left, right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec3Pad4<T> operator /(Vec3Pad4<T> left, T right) => Divide(left, right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static implicit operator (T X, T Y, T Z)(in Vec3Pad4<T> vec) => (vec.X, vec.Y, vec.Z);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static implicit operator Vec3Pad4<T>((T X, T Y, T Z) tuple) => new Vec3Pad4<T>(tuple.X, tuple.Y, tuple.Z);

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static Vector64<T> AsV64(in Vec3Pad4<T> v) => Unsafe.As<Vec3Pad4<T>, Vector64<T>>(ref Unsafe.AsRef(in v));

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static Vec3Pad4<T> FromV64(in Vector64<T> v) => Unsafe.As<Vector64<T>, Vec3Pad4<T>>(ref Unsafe.AsRef(in v));

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static Vector128<T> AsV128(in Vec3Pad4<T> v) => Unsafe.As<Vec3Pad4<T>, Vector128<T>>(ref Unsafe.AsRef(in v));

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static Vec3Pad4<T> FromV128(in Vector128<T> v) => Unsafe.As<Vector128<T>, Vec3Pad4<T>>(ref Unsafe.AsRef(in v));

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static Vector256<T> AsV256(in Vec3Pad4<T> v) => Unsafe.As<Vec3Pad4<T>, Vector256<T>>(ref Unsafe.AsRef(in v));

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static Vec3Pad4<T> FromV256(in Vector256<T> v) => Unsafe.As<Vector256<T>, Vec3Pad4<T>>(ref Unsafe.AsRef(in v));
}
