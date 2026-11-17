using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace OpenUL.Mathematics.Vectors;

using Elem = double;

[StructLayout(LayoutKind.Explicit)]
public record struct Vec3D : IVector3<Vec3D, Elem>, IVectorCross<Vec3D, Vec3D>
{
  [FieldOffset(sizeof(Elem) * 0)]
  private InlineArray3<Elem> Inline;

  [FieldOffset(sizeof(Elem) * 0)]
  public Elem X;
  [FieldOffset(sizeof(Elem) * 1)]
  public Elem Y;
  [FieldOffset(sizeof(Elem) * 2)]
  public Elem Z;


  public static Vec3D Create(Elem x, Elem y, Elem z) => new(x, y, z);
  public static Vec3D Create(Elem value) => new(value, value, value);
  public Vec3D(Elem x, Elem y, Elem z)
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

  Elem IVector2<Vec3D, Elem>.X { readonly get => X; set => X = value; }
  Elem IVector2<Vec3D, Elem>.Y { readonly get => Y; set => Y = value; }
  Elem IVector3<Vec3D, Elem>.Z { readonly get => Z; set => Z = value; }

  [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.AggressiveInlining)]
  private readonly Vector256<Elem> ToIntrinsicVector() => Vector256.Create(X, Y, Z, 0);

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static Vec3D FromIntrinsicVector(Vector256<Elem> vec)
    => new(vec.GetElement(0), vec.GetElement(1), vec.GetElement(2));

  public static T Dot<T>(Vec3D a, Vec3D b, NumberCreation creation = NumberCreation.Saturate)
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

  public static Vec3D Cross(Vec3D a, Vec3D b)
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

  public static Vec3D operator +(Vec3D value)
  {
    return value;
  }

  public static Vec3D operator +(Vec3D left, Vec3D right)
  {
    return FromIntrinsicVector(left.ToIntrinsicVector() + right.ToIntrinsicVector());
  }

  public static Vec3D operator -(Vec3D value)
  {
    return FromIntrinsicVector(-value.ToIntrinsicVector());
  }

  public static Vec3D operator -(Vec3D left, Vec3D right)
  {
    return FromIntrinsicVector(left.ToIntrinsicVector() - right.ToIntrinsicVector());
  }

  public static Vec3D operator *(Vec3D left, Vec3D right)
  {
    return FromIntrinsicVector(left.ToIntrinsicVector() * right.ToIntrinsicVector());
  }

  public static Vec3D operator /(Vec3D left, Vec3D right)
  {
    return FromIntrinsicVector(left.ToIntrinsicVector() / right.ToIntrinsicVector());
  }
}