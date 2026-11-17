using System.Numerics;

namespace OpenUL.Mathematics;

public static class Algorithm
{
  public static T GreatestCommonDenominator<T>(T a, T b)
    where T : INumber<T>
  {
    a = T.Abs(a);
    b = T.Abs(b);
    while (b != T.Zero)
    {
      (a, b) = (b, a % b);
    }
    return a;
  }

  public static (T Gcd, T X, T Y) ExtendedGreatestCommonDenominator<T>(T a, T b)
    where T : INumberBase<T>
  {
    T oldR = a, r = b;
    T oldS = T.One, s = T.Zero;
    T oldT = T.Zero, t = T.One;

    while (r != T.Zero)
    {
      T quotient = oldR / r;
      (oldR, r) = (r, oldR - quotient * r);
      (oldS, s) = (s, oldS - quotient * s);
      (oldT, t) = (t, oldT - quotient * t);
    }

    return (oldR, oldS, oldT);
  }

  public static T LeastCommonMultiple<T>(T a, T b)
    where T : INumber<T>
  {
    return a / GreatestCommonDenominator(a, b) * b;
  }
}