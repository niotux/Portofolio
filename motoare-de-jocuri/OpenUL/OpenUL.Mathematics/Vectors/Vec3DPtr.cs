using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace OpenUL.Mathematics.Vectors;

using Elem = double;

[StructLayout(LayoutKind.Explicit)]
public record struct Vec3DPtr : IVector3<Vec3DPtr, Elem>, IVectorCross<Vec3DPtr, Vec3DPtr>
{
  [FieldOffset(sizeof(Elem) * 0)]
  private InlineArray3<Elem> Inline;

  [FieldOffset(sizeof(Elem) * 0)]
  public Elem X;
  [FieldOffset(sizeof(Elem) * 1)]
  public Elem Y;
  [FieldOffset(sizeof(Elem) * 2)]
  public Elem Z;


  public static Vec3DPtr Create(Elem x, Elem y, Elem z) => new(x, y, z);
  public static Vec3DPtr Create(Elem value) => new(value, value, value);
  public Vec3DPtr(Elem x, Elem y, Elem z)
  {
    X = x;
    Y = y;
    Z = z;
  }

  public Elem this[int index]
  {
    readonly get => Inline[index];
    set => Inline[index] = value;
  }

  Elem IVector2<Vec3DPtr, Elem>.X { readonly get => X; set => X = value; }
  Elem IVector2<Vec3DPtr, Elem>.Y { readonly get => Y; set => Y = value; }
  Elem IVector3<Vec3DPtr, Elem>.Z { readonly get => Z; set => Z = value; }

  [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.AggressiveInlining)]
  private readonly Vector256<Elem> ToIntrinsicVector() => Vector256.Create(X, Y, Z, 0);
  [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.AggressiveInlining)]
  private static unsafe Vec3DPtr FromIntrinsicVector(in Vector256<Elem> vec)
  {
    fixed (Vector256<Elem>* ptr = &vec)
    {
      return *(Vec3DPtr*)ptr;
    }
  }

  public static T Dot<T>(Vec3DPtr a, Vec3DPtr b, NumberCreation creation = NumberCreation.Saturate)
    where T : INumber<T>
  {
    return Number.Convert<Elem, T>(Vector256.Dot(a.ToIntrinsicVector(), b.ToIntrinsicVector()));
  }

  public T LengthSquared<T>(NumberCreation creation = NumberCreation.Saturate)
    where T : INumber<T>
    => Dot<T>(this, this);

  public T Length<T>(NumberCreation creation = NumberCreation.Saturate)
    where T : INumber<T>, IRootFunctions<T>
    => T.Sqrt(LengthSquared<T>(creation));

  public static Vec3DPtr Cross(Vec3DPtr a, Vec3DPtr b)
  {
    if (Avx2.IsSupported)
    {
      const byte Yzxw = 0b11_00_10_01;
      const byte Zxyw = 0b11_01_00_10;

      var va = a.ToIntrinsicVector();
      var vb = b.ToIntrinsicVector();

      var a1 = Avx2.Permute4x64(va, Yzxw);
      var b1 = Avx2.Permute4x64(vb, Zxyw);
      var a2 = Avx2.Permute4x64(va, Zxyw);
      var b2 = Avx2.Permute4x64(vb, Yzxw);

      var v = a1 * b1 - a2 * b2;
      return FromIntrinsicVector(v);
    }
    if (Vector256.IsHardwareAccelerated)
    {
      var Yzxw = Vector256.Create(1L, 2L, 0L, 3L);
      var Zxyw = Vector256.Create(2L, 0L, 1L, 3L);

      var va = a.ToIntrinsicVector();
      var vb = b.ToIntrinsicVector();

      var a1 = Vector256.Shuffle(va, Yzxw);
      var b1 = Vector256.Shuffle(vb, Zxyw);
      var a2 = Vector256.Shuffle(va, Zxyw);
      var b2 = Vector256.Shuffle(vb, Yzxw);

      var v = a1 * b1 - a2 * b2;
      return FromIntrinsicVector(v);
    }
    return new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
  }

  public static Vec3DPtr operator +(Vec3DPtr value)
  {
    return value;
  }

  public static Vec3DPtr operator +(Vec3DPtr left, Vec3DPtr right)
  {
    return FromIntrinsicVector(left.ToIntrinsicVector() + right.ToIntrinsicVector());
  }

  public static Vec3DPtr operator -(Vec3DPtr value)
  {
    return FromIntrinsicVector(-value.ToIntrinsicVector());
  }

  public static Vec3DPtr operator -(Vec3DPtr left, Vec3DPtr right)
  {
    return FromIntrinsicVector(left.ToIntrinsicVector() - right.ToIntrinsicVector());
  }

  public static Vec3DPtr operator *(Vec3DPtr left, Vec3DPtr right)
  {
    return FromIntrinsicVector(left.ToIntrinsicVector() * right.ToIntrinsicVector());
  }

  public static Vec3DPtr operator /(Vec3DPtr left, Vec3DPtr right)
  {
    return FromIntrinsicVector(left.ToIntrinsicVector() / right.ToIntrinsicVector());
  }
}