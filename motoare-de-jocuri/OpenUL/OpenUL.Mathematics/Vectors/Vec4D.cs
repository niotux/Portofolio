using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace OpenUL.Mathematics.Vectors;

using Elem = double;

[StructLayout(LayoutKind.Explicit)]
public record struct Vec4D : IVector4<Vec4D, Elem>
{
  [FieldOffset(0)]
  private InlineArray3<Elem> Inline;

  [FieldOffset(0)]
  private Vector256<double> Vector;

  [FieldOffset(sizeof(Elem) * 0)]
  public Elem X;
  [FieldOffset(sizeof(Elem) * 1)]
  public Elem Y;
  [FieldOffset(sizeof(Elem) * 2)]
  public Elem Z;
  [FieldOffset(sizeof(Elem) * 3)]
  public Elem W;


  public static Vec4D Create(Elem x, Elem y, Elem z, Elem w) => new(x, y, z, w);
  public static Vec4D Create(Elem value) => new(value, value, value, value);
  public Vec4D(Elem x, Elem y, Elem z, Elem w)
  {
    Vector = Vector256.Create(x, y, z, w);
  }

  public Elem this[int index]
  {
    readonly get => Inline[index];
    set => Inline[index] = value;
  }

  Elem IVector2<Vec4D, Elem>.X { readonly get => X; set => X = value; }
  Elem IVector2<Vec4D, Elem>.Y { readonly get => Y; set => Y = value; }
  Elem IVector3<Vec4D, Elem>.Z { readonly get => Z; set => Z = value; }
  Elem IVector4<Vec4D, Elem>.W { readonly get => Z; set => Z = value; }

  public static T Dot<T>(Vec4D a, Vec4D b, NumberCreation creation = NumberCreation.Saturate)
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

  public static Vec4D operator +(Vec4D value)
  {
    return value;
  }

  public static Vec4D operator +(Vec4D left, Vec4D right)
  {
    return new Vec4D() with { Vector = left.Vector + right.Vector };
  }

  public static Vec4D operator -(Vec4D value)
  {
    return new Vec4D() with { Vector = -value.Vector };
  }

  public static Vec4D operator -(Vec4D left, Vec4D right)
  {
    return new Vec4D() with { Vector = left.Vector - right.Vector };
  }

  public static Vec4D operator *(Vec4D left, Vec4D right)
  {
    return new Vec4D() with { Vector = left.Vector * right.Vector };
  }

  public static Vec4D operator /(Vec4D left, Vec4D right)
  {
    return new Vec4D() with { Vector = left.Vector / right.Vector };
  }
}