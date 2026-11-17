using System.Numerics;
using System.Runtime.InteropServices;

namespace OpenUL.Mathematics.Vectors;

using Elem = float;

[StructLayout(LayoutKind.Explicit)]
public record struct Vec3F : IVector3<Vec3F, Elem>, IVectorCross<Vec3F, Vec3F>
{
  [FieldOffset(0)]
  private System.Numerics.Vector3 Vector;

  [FieldOffset(sizeof(Elem) * 0)]
  public Elem X;
  [FieldOffset(sizeof(Elem) * 1)]
  public Elem Y;
  [FieldOffset(sizeof(Elem) * 2)]
  public Elem Z;


  public static Vec3F Create(Elem x, Elem y, Elem z) => new(x, y, z);
  public static Vec3F Create(Elem value) => new(value, value, value);
  public Vec3F(Elem x, Elem y, Elem z)
  {
    Vector = new(x, y, z);
  }


  public Elem this[int index]
  {
    readonly get => Vector[index];
    set => Vector[index] = value;
  }

  public static T Dot<T>(Vec3F a, Vec3F b, NumberCreation creation = NumberCreation.Saturate)
    where T : INumber<T>
  {
    return Number.Convert<Elem, T>(System.Numerics.Vector3.Dot(a.Vector, b.Vector));
  }

  public T LengthSquared<T>(NumberCreation creation = NumberCreation.Saturate)
    where T : INumber<T>
    => Dot<T>(this, this);

  public T Length<T>(NumberCreation creation = NumberCreation.Saturate)
    where T : INumber<T>, IRootFunctions<T>
    => T.Sqrt(LengthSquared<T>(creation));

  Elem IVector2<Vec3F, Elem>.X { readonly get => X; set => X = value; }
  Elem IVector2<Vec3F, Elem>.Y { readonly get => Y; set => Y = value; }
  Elem IVector3<Vec3F, Elem>.Z { readonly get => Z; set => Z = value; }

  public static Vec3F Cross(Vec3F a, Vec3F b)
  {
    return new Vec3F() with { Vector = System.Numerics.Vector3.Cross(a.Vector, b.Vector) };
  }

  public static Vec3F operator +(Vec3F value)
  {
    return value;
  }

  public static Vec3F operator +(Vec3F left, Vec3F right)
  {
    return new Vec3F() with { Vector = left.Vector + right.Vector };
  }

  public static Vec3F operator -(Vec3F value)
  {
    return new Vec3F() with { Vector = -value.Vector };
  }

  public static Vec3F operator -(Vec3F left, Vec3F right)
  {
    return new Vec3F() with { Vector = left.Vector - right.Vector };
  }

  public static Vec3F operator *(Vec3F left, Vec3F right)
  {
    return new Vec3F() with { Vector = left.Vector * right.Vector };
  }

  public static Vec3F operator /(Vec3F left, Vec3F right)
  {
    return new Vec3F() with { Vector = left.Vector / right.Vector };
  }
}