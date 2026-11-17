using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace OpenUL.Mathematics.Vectors;

using Elem = int;

[StructLayout(LayoutKind.Explicit)]
public record struct Vec2I : IVector2<Vec2I, Elem>
{
  [FieldOffset(0)]
  private Vector64<int> Vector;

  [FieldOffset(sizeof(Elem) * 0)]
  public int X;
  [FieldOffset(sizeof(Elem) * 1)]
  public int Y;


  public static Vec2I Create(int x, int y) => new(x, y);
  public static Vec2I Create(int value) => new(value, value);
  public Vec2I(Elem x, Elem y)
  {
    X = x;
    Y = y;
  }


  int IVector2<Vec2I, int>.X { readonly get => X; set => X = value; }
  int IVector2<Vec2I, int>.Y { readonly get => Y; set => Y = value; }

  public int this[int index]
  {
    readonly get => Vector.GetElement(index);
    set => Vector = Vector.WithElement(index, value);
  }

  public static T Dot<T>(Vec2I left, Vec2I right, NumberCreation creation = NumberCreation.Saturate)
    where T : System.Numerics.INumber<T>
  {
    return Number.Convert<Elem, T>(Vector64.Dot(left.Vector, right.Vector));
  }

  public T LengthSquared<T>(NumberCreation creation = NumberCreation.Saturate)
    where T : INumber<T>
    => Dot<T>(this, this);

  public T Length<T>(NumberCreation creation = NumberCreation.Saturate)
    where T : INumber<T>, IRootFunctions<T>
    => T.Sqrt(LengthSquared<T>(creation));

  public static Vec2I operator +(Vec2I left, Vec2I right)
  {
    return new Vec2I() with { Vector = left.Vector + right.Vector };
  }

  public static Vec2I operator -(Vec2I left, Vec2I right)
  {
    return new Vec2I() with { Vector = left.Vector - right.Vector };
  }

  public static Vec2I operator *(Vec2I left, Vec2I right)
  {
    return new Vec2I() with { Vector = left.Vector * right.Vector };
  }

  public static Vec2I operator /(Vec2I left, Vec2I right)
  {
    return new Vec2I() with { Vector = left.Vector / right.Vector };
  }

  public static Vec2I operator +(Vec2I value)
  {
    return value;
  }

  public static Vec2I operator -(Vec2I value)
  {
    return new Vec2I() with { Vector = -value.Vector };
  }
}