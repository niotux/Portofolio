using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace OpenUL.Mathematics.Vectors;

using Elem = double;

[StructLayout(LayoutKind.Explicit)]
public record struct Vec3_4D : IVector3<Vec3_4D, Elem>, IVectorCross<Vec3_4D, Vec3_4D>
{
  [FieldOffset(0)]
  private Vector256<double> Vector;

  [FieldOffset(sizeof(Elem) * 0)]
  public Elem X;
  [FieldOffset(sizeof(Elem) * 1)]
  public Elem Y;
  [FieldOffset(sizeof(Elem) * 2)]
  public Elem Z;


  public static Vec3_4D Create(Elem x, Elem y, Elem z) => new(x, y, z);
  public static Vec3_4D Create(Elem value) => new(value, value, value);
  public Vec3_4D(Elem x, Elem y, Elem z)
  {
    Vector = Vector256.Create(x, y, z, 0);
  }

  public Elem this[int index]
  {
    readonly get => Vector[index];
    set => Vector = Vector.WithElement(index, value);
  }

  Elem IVector2<Vec3_4D, Elem>.X { readonly get => X; set => X = value; }
  Elem IVector2<Vec3_4D, Elem>.Y { readonly get => Y; set => Y = value; }
  Elem IVector3<Vec3_4D, Elem>.Z { readonly get => Z; set => Z = value; }

  public static T Dot<T>(Vec3_4D a, Vec3_4D b, NumberCreation creation = NumberCreation.Saturate)
    where T : INumber<T>
  {
    return Number.Convert<Elem, T>(Vector256.Dot(a.Vector, b.Vector));
  }

  public T LengthSquared<T>(NumberCreation creation = NumberCreation.Saturate)
    where T : INumber<T>
    => Dot<T>(this, this);

  public T Length<T>(NumberCreation creation = NumberCreation.Saturate)
    where T : INumber<T>, IRootFunctions<T>
    => T.Sqrt(LengthSquared<T>(creation));

  public static Vec3_4D Cross(Vec3_4D a, Vec3_4D b)
  {
    if (Avx2.IsSupported)
    {
      const byte Yzxw = 0b11_00_10_01;
      const byte Zxyw = 0b11_01_00_10;

      var a1 = Avx2.Permute4x64(a.Vector, Yzxw);
      var b1 = Avx2.Permute4x64(b.Vector, Zxyw);
      var a2 = Avx2.Permute4x64(a.Vector, Zxyw);
      var b2 = Avx2.Permute4x64(b.Vector, Yzxw);

      return new Vec3_4D() with { Vector = a1 * b1 - a2 * b2 };
    }

    return new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
  }
  public static Vec3_4D operator +(Vec3_4D value)
  {
    return value;
  }

  public static Vec3_4D operator +(Vec3_4D left, Vec3_4D right)
  {
    return new Vec3_4D() with { Vector = left.Vector + right.Vector };
  }

  public static Vec3_4D operator -(Vec3_4D value)
  {
    return new Vec3_4D() with { Vector = -value.Vector };
  }

  public static Vec3_4D operator -(Vec3_4D left, Vec3_4D right)
  {
    return new Vec3_4D() with { Vector = left.Vector - right.Vector };
  }

  public static Vec3_4D operator *(Vec3_4D left, Vec3_4D right)
  {
    return new Vec3_4D() with { Vector = left.Vector * right.Vector };
  }

  public static Vec3_4D operator /(Vec3_4D left, Vec3_4D right)
  {
    return new Vec3_4D() with { Vector = left.Vector / right.Vector };
  }
}