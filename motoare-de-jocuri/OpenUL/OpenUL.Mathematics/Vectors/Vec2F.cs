using System.Numerics;
using System.Runtime.InteropServices;

namespace OpenUL.Mathematics.Vectors;

using Elem = float;

[StructLayout(LayoutKind.Explicit)]
public record struct Vec2F : IVector2<Vec2F, Elem>, IVectorCross<Vec2F, Vec3F>
{
  [FieldOffset(0)]
  private System.Numerics.Vector2 Vector;

  [FieldOffset(sizeof(Elem) * 0)]
  public Elem X;
  [FieldOffset(sizeof(Elem) * 1)]
  public Elem Y;


  public static Vec2F Create(Elem x, Elem y) => new(x, y);
  public static Vec2F Create(Elem value) => new(value, value);
  public Vec2F(Elem x, Elem y)
  {
    Vector = new(x, y);
  }


  public Elem this[int index]
  {
    readonly get => Vector[index];
    set => Vector[index] = value;
  }


  Elem IVector2<Vec2F, Elem>.X { readonly get => X; set => X = value; }
  Elem IVector2<Vec2F, Elem>.Y { readonly get => Y; set => Y = value; }

  public static T Dot<T>(Vec2F a, Vec2F b, NumberCreation creation = NumberCreation.Saturate)
    where T : INumber<T>
  {
    return Number.Convert<Elem, T>(System.Numerics.Vector2.Dot(a.Vector, b.Vector));
  }

  public T LengthSquared<T>(NumberCreation creation = NumberCreation.Saturate)
    where T : INumber<T>
    => Dot<T>(this, this);

  public T Length<T>(NumberCreation creation = NumberCreation.Saturate)
    where T : INumber<T>, IRootFunctions<T>
    => T.Sqrt(LengthSquared<T>(creation));

  public static Vec3F Cross(Vec2F a, Vec2F b)
  {
    return new(0, 0, System.Numerics.Vector2.Cross(a.Vector, b.Vector));
  }


  public static Vec2F operator +(Vec2F value)
  {
    return value;
  }

  public static Vec2F operator +(Vec2F left, Vec2F right)
  {
    return new Vec2F() with { Vector = left.Vector + right.Vector };
  }

  public static Vec2F operator -(Vec2F value)
  {
    return new Vec2F() with { Vector = -value.Vector };
  }

  public static Vec2F operator -(Vec2F left, Vec2F right)
  {
    return new Vec2F() with { Vector = left.Vector - right.Vector };
  }

  public static Vec2F operator *(Vec2F left, Vec2F right)
  {
    return new Vec2F() with { Vector = left.Vector * right.Vector };
  }

  public static Vec2F operator /(Vec2F left, Vec2F right)
  {
    return new Vec2F() with { Vector = left.Vector / right.Vector };
  }
}