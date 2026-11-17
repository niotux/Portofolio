using System.Numerics;
using System.Runtime.InteropServices;

namespace OpenUL.Mathematics.Vectors;

using Elem = float;

[StructLayout(LayoutKind.Explicit)]
public record struct Vec4F : IVector4<Vec4F, Elem>, IVectorCross<Vec4F, Vec4F>
{
  [FieldOffset(0)]
  private System.Numerics.Vector4 Vector;

  [FieldOffset(sizeof(Elem) * 0)]
  public Elem X;
  [FieldOffset(sizeof(Elem) * 1)]
  public Elem Y;
  [FieldOffset(sizeof(Elem) * 2)]
  public Elem Z;
  [FieldOffset(sizeof(Elem) * 3)]
  public Elem W;


  public static Vec4F Create(Elem x, Elem y, Elem z, Elem w) => new(x, y, z, w);
  public static Vec4F Create(Elem value) => new(value, value, value, value);
  public Vec4F(Elem x, Elem y, Elem z, Elem w)
  {
    Vector = new(x, y, z, w);
  }

  public Elem this[int index]
  {
    readonly get => Vector[index];
    set => Vector[index] = value;
  }

  Elem IVector2<Vec4F, Elem>.X { readonly get => X; set => X = value; }
  Elem IVector2<Vec4F, Elem>.Y { readonly get => Y; set => Y = value; }
  Elem IVector3<Vec4F, Elem>.Z { readonly get => Z; set => Z = value; }
  Elem IVector4<Vec4F, Elem>.W { readonly get => W; set => W = value; }

  public static T Dot<T>(Vec4F a, Vec4F b, NumberCreation creation = NumberCreation.Saturate)
    where T : INumber<T>
  {
    return Number.Convert<Elem, T>(System.Numerics.Vector4.Dot(a.Vector, b.Vector));
  }

  public T LengthSquared<T>(NumberCreation creation = NumberCreation.Saturate)
    where T : INumber<T>
    => Dot<T>(this, this);

  public T Length<T>(NumberCreation creation = NumberCreation.Saturate)
    where T : INumber<T>, IRootFunctions<T>
    => T.Sqrt(LengthSquared<T>(creation));

  public static Vec4F Cross(Vec4F a, Vec4F b)
  {
    return new Vec4F() with { Vector = System.Numerics.Vector4.Cross(a.Vector, b.Vector) };
  }

  public static Vec4F operator +(Vec4F value)
  {
    return value;
  }

  public static Vec4F operator +(Vec4F left, Vec4F right)
  {
    return new Vec4F() with { Vector = left.Vector + right.Vector };
  }

  public static Vec4F operator -(Vec4F value)
  {
    return new Vec4F() with { Vector = -value.Vector };
  }

  public static Vec4F operator -(Vec4F left, Vec4F right)
  {
    return new Vec4F() with { Vector = left.Vector - right.Vector };
  }

  public static Vec4F operator *(Vec4F left, Vec4F right)
  {
    return new Vec4F() with { Vector = left.Vector * right.Vector };
  }

  public static Vec4F operator /(Vec4F left, Vec4F right)
  {
    return new Vec4F() with { Vector = left.Vector / right.Vector };
  }
}