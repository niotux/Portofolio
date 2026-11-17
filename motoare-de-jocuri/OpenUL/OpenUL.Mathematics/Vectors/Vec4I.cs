using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace OpenUL.Mathematics.Vectors;

using Elem = int;

[StructLayout(LayoutKind.Explicit)]
public record struct Vec4I : IVector4<Vec4I, Elem>
{
  [FieldOffset(0)]
  private InlineArray3<Elem> Inline;

  [FieldOffset(0)]
  private Vector128<int> Vector;

  [FieldOffset(sizeof(Elem) * 0)]
  public int X;
  [FieldOffset(sizeof(Elem) * 1)]
  public int Y;
  [FieldOffset(sizeof(Elem) * 2)]
  public int Z;
  [FieldOffset(sizeof(Elem) * 3)]
  public int W;


  public static Vec4I Create(Elem x, Elem y, Elem z, Elem w) => new(x, y, z, w);
  public static Vec4I Create(int value) => new(value, value, value, value);
  public Vec4I(Elem x, Elem y, Elem z, Elem w)
  {
    X = x;
    Y = y;
    Z = z;
    W = w;
  }


  int IVector2<Vec4I, int>.X { readonly get => X; set => X = value; }
  int IVector2<Vec4I, int>.Y { readonly get => Y; set => Y = value; }
  int IVector3<Vec4I, int>.Z { readonly get => Z; set => Z = value; }
  int IVector4<Vec4I, int>.W { readonly get => W; set => W = value; }

  public int this[int index]
  {
    readonly get => Vector.GetElement(index);
    set => Vector = Vector.WithElement(index, value);
  }

  public static T Dot<T>(Vec4I left, Vec4I right, NumberCreation creation = NumberCreation.Saturate)
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

  public static Vec4I operator +(Vec4I left, Vec4I right)
  {
    return new Vec4I() with { Vector = left.Vector + right.Vector };
  }

  public static Vec4I operator -(Vec4I left, Vec4I right)
  {
    return new Vec4I() with { Vector = left.Vector - right.Vector };
  }

  public static Vec4I operator *(Vec4I left, Vec4I right)
  {
    return new Vec4I() with { Vector = left.Vector * right.Vector };
  }

  public static Vec4I operator /(Vec4I left, Vec4I right)
  {
    return new Vec4I() with { Vector = left.Vector / right.Vector };
  }

  public static Vec4I operator +(Vec4I value)
  {
    return value;
  }

  public static Vec4I operator -(Vec4I value)
  {
    return new Vec4I() with { Vector = -value.Vector };
  }
}