using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace OpenUL.Mathematics.Vectors;

using Elem = int;

[StructLayout(LayoutKind.Explicit)]
public record struct Vec3_4I : IVector3<Vec3_4I, Elem>
{
  [FieldOffset(0)]
  private Vector128<int> Vector;

  [FieldOffset(sizeof(Elem) * 0)]
  public int X;
  [FieldOffset(sizeof(Elem) * 1)]
  public int Y;
  [FieldOffset(sizeof(Elem) * 2)]
  public int Z;


  public static Vec3_4I Create(Elem x, Elem y, Elem z) => new(x, y, z);
  public static Vec3_4I Create(int value) => new(value, value, value);
  public Vec3_4I(Elem x, Elem y, Elem z)
  {
    X = x;
    Y = y;
    Z = z;
  }


  int IVector2<Vec3_4I, int>.X { readonly get => X; set => X = value; }
  int IVector2<Vec3_4I, int>.Y { readonly get => Y; set => Y = value; }
  int IVector3<Vec3_4I, int>.Z { readonly get => Z; set => Z = value; }

  public int this[int index]
  {
    readonly get => Vector.GetElement(index);
    set => Vector = Vector.WithElement(index, value);
  }

  public static T Dot<T>(Vec3_4I left, Vec3_4I right, NumberCreation creation = NumberCreation.Saturate)
    where T : System.Numerics.INumber<T>
  {
    return Number.Convert<Elem, T>(Vector128.Dot(left.Vector, right.Vector));
  }

  public T LengthSquared<T>(NumberCreation creation = NumberCreation.Saturate)
    where T : INumber<T>
    => Dot<T>(this, this);

  public T Length<T>(NumberCreation creation = NumberCreation.Saturate)
    where T : INumber<T>, IRootFunctions<T>
    => T.Sqrt(LengthSquared<T>(creation));

  public static Vec3_4I operator +(Vec3_4I left, Vec3_4I right)
  {
    return new Vec3_4I() with { Vector = left.Vector + right.Vector };
  }

  public static Vec3_4I operator -(Vec3_4I left, Vec3_4I right)
  {
    return new Vec3_4I() with { Vector = left.Vector - right.Vector };
  }

  public static Vec3_4I operator *(Vec3_4I left, Vec3_4I right)
  {
    return new Vec3_4I() with { Vector = left.Vector * right.Vector };
  }

  public static Vec3_4I operator /(Vec3_4I left, Vec3_4I right)
  {
    return new Vec3_4I() with { Vector = left.Vector / right.Vector };
  }

  public static Vec3_4I operator +(Vec3_4I value)
  {
    return value;
  }

  public static Vec3_4I operator -(Vec3_4I value)
  {
    return new Vec3_4I() with { Vector = -value.Vector };
  }
}