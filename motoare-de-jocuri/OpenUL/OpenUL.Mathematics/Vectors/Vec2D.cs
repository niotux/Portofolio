using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace OpenUL.Mathematics.Vectors;

using Elem = double;

[StructLayout(LayoutKind.Explicit)]
public record struct Vec2D : IVector2<Vec2D, Elem>, IVectorCross<Vec2D, Vec3_4D>
{
  [FieldOffset(0)]
  private Vector128<Elem> Vector;

  [FieldOffset(sizeof(Elem) * 0)]
  public Elem X;
  [FieldOffset(sizeof(Elem) * 1)]
  public Elem Y;


  public static Vec2D Create(Elem x, Elem y) => new(x, y);
  public static Vec2D Create(Elem value) => new(value, value);
  public Vec2D(Elem x, Elem y)
  {
    Vector = Vector128.Create(x, y);
  }


  public Elem this[int index]
  {
    readonly get => Vector[index];
    set => Vector = Vector.WithElement(index, value);
  }

  Elem IVector2<Vec2D, Elem>.X { readonly get => X; set => X = value; }
  Elem IVector2<Vec2D, Elem>.Y { readonly get => Y; set => Y = value; }

  public static T Dot<T>(Vec2D a, Vec2D b, NumberCreation creation = NumberCreation.Saturate)
    where T : INumber<T>
  {
    return Number.Convert<Elem, T>(Vector128.Dot(a.Vector, b.Vector));
  }


  public static Vec3_4D Cross(Vec2D a, Vec2D b)
  {
    b.Vector = Vector128.Shuffle(b.Vector, Vector128.Create(1, 0));
    a *= b;
    return new(0, 0, a.X - a.Y);
  }

  public T LengthSquared<T>(NumberCreation creation = NumberCreation.Saturate)
    where T : INumber<T>
    => Dot<T>(this, this);

  public T Length<T>(NumberCreation creation = NumberCreation.Saturate)
    where T : INumber<T>, IRootFunctions<T>
    => T.Sqrt(LengthSquared<T>(creation));

  public static Vec2D operator +(Vec2D value)
  {
    return value;
  }

  public static Vec2D operator +(Vec2D left, Vec2D right)
  {
    return new Vec2D() with { Vector = left.Vector + right.Vector };
  }

  public static Vec2D operator -(Vec2D value)
  {
    return new Vec2D() with { Vector = -value.Vector };
  }

  public static Vec2D operator -(Vec2D left, Vec2D right)
  {
    return new Vec2D() with { Vector = left.Vector - right.Vector };
  }

  public static Vec2D operator *(Vec2D left, Vec2D right)
  {
    return new Vec2D() with { Vector = left.Vector * right.Vector };
  }

  public static Vec2D operator /(Vec2D left, Vec2D right)
  {
    return new Vec2D() with { Vector = left.Vector / right.Vector };
  }
}