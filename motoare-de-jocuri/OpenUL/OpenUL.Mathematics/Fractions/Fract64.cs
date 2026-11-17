using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;

namespace OpenUL.Mathematics.Fractions;

public readonly struct Fract64 : INumber<Fract64>
{
  public readonly int Numerator;
  public readonly uint Denominator;

  public Fract64(int numerator, int denominator)
  {
    if (denominator == 0)
    {
      throw new DivideByZeroException("Denominator cannot be zero");
    }
    if (denominator < 0)
    {
      Numerator = -numerator;
      Denominator = (uint)-denominator;
    }
    else
    {
      Numerator = numerator;
      Denominator = (uint)denominator;
    }
  }

  public Fract64(int upper, uint lower)
  {
    if (lower == 0)
    {
      throw new DivideByZeroException("Denominator cannot be zero");
    }
    Numerator = upper;
    Denominator = lower;
  }

  public Fract64 Reduce()
  {
    // Avoid overflow on int.MinValue by widening to long before taking the absolute value.
    uint absNumerator = (uint)Math.Abs((long)Numerator);
    uint gcd = absNumerator == 0
      ? Denominator
      : Algorithm.GreatestCommonDenominator(absNumerator, Denominator);
    if (gcd == 0)
    {
      gcd = 1;
    }
    return new(Numerator / (int)gcd, Denominator / gcd);
  }

  public double ToDouble() => (double)Numerator / Denominator;
  public float ToSingle() => (float)Numerator / Denominator;

  public int CompareTo(object? obj)
  {
    if (obj is null)
    {
      return 1;
    }
    if (obj is Fract64 other)
    {
      return CompareTo(other);
    }
    throw new ArgumentException($"Object must be of type {nameof(Fract64)}", nameof(obj));
  }

  public int CompareTo(Fract64 other)
  {
    if (Denominator == other.Denominator)
    {
      return Numerator.CompareTo(other.Numerator);
    }

    long lhs = Numerator * other.Denominator;
    long rhs = other.Numerator * Denominator;
    return lhs.CompareTo(rhs);
  }

  public static Fract64 Abs(Fract64 value)
    => new(checked(Number.Abs(value.Numerator)), value.Denominator);

  public static bool IsCanonical(Fract64 value)
  {
    if (value.Numerator == 0)
    {
      return value.Denominator == 1;
    }
    uint absNumerator = (uint)Number.Abs(value.Numerator);
    uint gcd = Algorithm.GreatestCommonDenominator(absNumerator, value.Denominator);
    return gcd == 1;
  }

  public static bool IsComplexNumber(Fract64 value) => false;

  public static bool IsEvenInteger(Fract64 value)
  {
    var reduced = value.Reduce();
    return reduced.Denominator == 1 && reduced.Numerator % 2 == 0;
  }

  public static bool IsFinite(Fract64 value) => true;

  public static bool IsImaginaryNumber(Fract64 value) => false;

  public static bool IsInfinity(Fract64 value) => false;

  public static bool IsInteger(Fract64 value) => value.Reduce().Denominator == 1;

  public static bool IsNaN(Fract64 value) => false;

  public static bool IsNegative(Fract64 value) => value.Numerator < 0;

  public static bool IsNegativeInfinity(Fract64 value) => false;

  public static bool IsNormal(Fract64 value) => value.Numerator != 0;

  public static bool IsOddInteger(Fract64 value)
  {
    var reduced = value.Reduce();
    return reduced.Denominator == 1 && reduced.Numerator % 2 != 0;
  }

  public static bool IsPositive(Fract64 value) => value.Numerator >= 0;

  public static bool IsPositiveInfinity(Fract64 value) => false;

  public static bool IsRealNumber(Fract64 value) => true;

  public static bool IsSubnormal(Fract64 value) => false;

  public static bool IsZero(Fract64 value) => value.Numerator == 0;

  public static Fract64 MaxMagnitude(Fract64 x, Fract64 y)
  {
    Fract64 ax = Abs(x);
    Fract64 ay = Abs(y);
    if (ax > ay)
    {
      return x;
    }
    if (ax == ay)
    {
      return IsNegative(x) ? y : x;
    }
    return y;
  }

  public static Fract64 MaxMagnitudeNumber(Fract64 x, Fract64 y) => MaxMagnitude(x, y);

  public static Fract64 MinMagnitude(Fract64 x, Fract64 y)
  {
    Fract64 ax = Abs(x);
    Fract64 ay = Abs(y);
    if (ax < ay)
    {
      return x;
    }
    if (ax == ay)
    {
      return IsNegative(x) ? x : y;
    }
    return y;
  }

  public static Fract64 MinMagnitudeNumber(Fract64 x, Fract64 y) => MinMagnitude(x, y);

  public static Fract64 Parse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider)
  {
    if (TryParse(s, style, provider, out var result))
    {
      return result;
    }
    throw new FormatException($"Could not parse '{s.ToString()}' as a {nameof(Fract64)}");
  }

  public static Fract64 Parse(string s, NumberStyles style, IFormatProvider? provider)
  {
    ArgumentNullException.ThrowIfNull(s);
    return Parse(s.AsSpan(), style, provider);
  }

  public static bool TryConvertFromChecked<TOther>(TOther value, [MaybeNullWhen(false)] out Fract64 result) where TOther : INumberBase<TOther>
  {
    try
    {
      int numerator = int.CreateChecked(value);
      result = new Fract64(numerator, 1);
      return true;
    }
    catch (Exception ex) when (ex is OverflowException or NotSupportedException or FormatException)
    {
      result = default;
      return false;
    }
  }

  public static bool TryConvertFromSaturating<TOther>(TOther value, [MaybeNullWhen(false)] out Fract64 result) where TOther : INumberBase<TOther>
  {
    try
    {
      int numerator = int.CreateSaturating(value);
      result = new Fract64(numerator, 1);
      return true;
    }
    catch (Exception ex) when (ex is NotSupportedException or FormatException)
    {
      result = default;
      return false;
    }
  }

  public static bool TryConvertFromTruncating<TOther>(TOther value, [MaybeNullWhen(false)] out Fract64 result) where TOther : INumberBase<TOther>
  {
    try
    {
      int numerator = int.CreateTruncating(value);
      result = new Fract64(numerator, 1);
      return true;
    }
    catch (Exception ex) when (ex is NotSupportedException or FormatException)
    {
      result = default;
      return false;
    }
  }

  public static bool TryConvertToChecked<TOther>(Fract64 value, [MaybeNullWhen(false)] out TOther result) where TOther : INumberBase<TOther>
  {
    try
    {
      result = TOther.CreateChecked(value.ToDouble());
      return true;
    }
    catch (Exception ex) when (ex is OverflowException or NotSupportedException or FormatException)
    {
      result = default;
      return false;
    }
  }

  public static bool TryConvertToSaturating<TOther>(Fract64 value, [MaybeNullWhen(false)] out TOther result) where TOther : INumberBase<TOther>
  {
    try
    {
      result = TOther.CreateSaturating(value.ToDouble());
      return true;
    }
    catch (Exception ex) when (ex is NotSupportedException or FormatException)
    {
      result = default;
      return false;
    }
  }

  public static bool TryConvertToTruncating<TOther>(Fract64 value, [MaybeNullWhen(false)] out TOther result) where TOther : INumberBase<TOther>
  {
    try
    {
      result = TOther.CreateTruncating(value.ToDouble());
      return true;
    }
    catch (Exception ex) when (ex is NotSupportedException or FormatException)
    {
      result = default;
      return false;
    }
  }

  public static bool TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, [MaybeNullWhen(false)] out Fract64 result)
  {
    int slashIndex = s.IndexOf('/');
    if (slashIndex >= 0)
    {
      var numeratorSpan = s[..slashIndex];
      var denominatorSpan = s[(slashIndex + 1)..];
      if (int.TryParse(numeratorSpan, style, provider, out int numerator) &&
          int.TryParse(denominatorSpan, style, provider, out int denominator) &&
          denominator != 0)
      {
        result = new Fract64(numerator, denominator);
        return true;
      }

      result = default;
      return false;
    }

    if (int.TryParse(s, style, provider, out int wholeValue))
    {
      result = new Fract64(wholeValue, 1);
      return true;
    }

    result = default;
    return false;
  }

  public static bool TryParse([NotNullWhen(true)] string? s, NumberStyles style, IFormatProvider? provider, [MaybeNullWhen(false)] out Fract64 result)
  {
    if (s is null)
    {
      result = default;
      return false;
    }
    return TryParse(s.AsSpan(), style, provider, out result);
  }

  public bool Equals(Fract64 other)
  {
    if (Denominator == other.Denominator)
    {
      return Numerator == other.Numerator;
    }
    return (long)Numerator * other.Denominator == (long)other.Numerator * Denominator;
  }

  public override bool Equals(object? obj) => obj is Fract64 other && Equals(other);

  public override int GetHashCode()
  {
    var reduced = Reduce();
    return HashCode.Combine(reduced.Numerator, reduced.Denominator);
  }

  public override string ToString() => ToString(null, null);

  public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
  {
    string formatted = ToString(format.IsEmpty ? null : format.ToString(), provider);
    if (formatted.Length > destination.Length)
    {
      charsWritten = 0;
      return false;
    }
    formatted.AsSpan().CopyTo(destination);
    charsWritten = formatted.Length;
    return true;
  }

  public string ToString(string? format, IFormatProvider? formatProvider)
  {
    if (Denominator == 1)
    {
      return Numerator.ToString(format, formatProvider);
    }
    return $"{Numerator.ToString(format, formatProvider)}/{Denominator.ToString(format, formatProvider)}";
  }

  public static Fract64 Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
    => Parse(s, NumberStyles.Integer, provider);

  public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, [MaybeNullWhen(false)] out Fract64 result)
    => TryParse(s, NumberStyles.Integer, provider, out result);

  public static Fract64 Parse(string s, IFormatProvider? provider)
    => Parse(s, NumberStyles.Integer, provider);

  public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out Fract64 result)
    => TryParse(s, NumberStyles.Integer, provider, out result);

  public static implicit operator Fract64(int num)
    => new(num, 1);

  public static bool operator >(Fract64 left, Fract64 right) => left.CompareTo(right) > 0;

  public static bool operator >=(Fract64 left, Fract64 right) => left.CompareTo(right) >= 0;

  public static bool operator <(Fract64 left, Fract64 right) => left.CompareTo(right) < 0;

  public static bool operator <=(Fract64 left, Fract64 right) => left.CompareTo(right) <= 0;

  public static Fract64 operator %(Fract64 left, Fract64 right)
  {
    if (left.Denominator == right.Denominator)
    {
      return new(left.Numerator % right.Numerator, left.Denominator);
    }

    uint lcm = Algorithm.LeastCommonMultiple(left.Denominator, right.Denominator);
    return new((int)(lcm / left.Denominator) * left.Numerator % ((int)(lcm / right.Denominator) * right.Numerator), lcm);
  }

  public static Fract64 operator +(Fract64 left, Fract64 right)
  {
    if (left.Denominator == right.Denominator)
    {
      return new(left.Numerator + right.Numerator, left.Denominator);
    }

    uint lcm = Algorithm.LeastCommonMultiple(left.Denominator, right.Denominator);
    return new((int)(lcm / left.Denominator) * left.Numerator + (int)(lcm / right.Denominator) * right.Numerator, lcm);
  }

  public static Fract64 operator --(Fract64 value)
    => new(value.Numerator - (int)value.Denominator, value.Denominator);

  public static Fract64 operator /(Fract64 left, Fract64 right)
  {
    if (right.Numerator == 0)
    {
      throw new DivideByZeroException("Cannot divide by zero");
    }

    long num = (long)left.Numerator * right.Denominator;
    long den = (long)left.Denominator * right.Numerator;
    if (den < 0)
    {
      num = -num;
      den = -den;
    }

    return new((int)num, (uint)den);
  }

  public static bool operator ==(Fract64 left, Fract64 right) => left.Equals(right);

  public static bool operator !=(Fract64 left, Fract64 right) => !left.Equals(right);

  public static Fract64 operator ++(Fract64 value)
    => new(value.Numerator + (int)value.Denominator, value.Denominator);

  public static Fract64 operator *(Fract64 left, Fract64 right)
  {
    long num = (long)left.Numerator * right.Numerator;
    long den = (long)left.Denominator * right.Denominator;
    return new((int)num, (uint)den);
  }

  public static Fract64 operator -(Fract64 left, Fract64 right)
  {
    if (left.Denominator == right.Denominator)
    {
      return new(left.Numerator - right.Numerator, left.Denominator);
    }

    uint lcm = Algorithm.LeastCommonMultiple(left.Denominator, right.Denominator);
    return new((int)(lcm / left.Denominator) * left.Numerator - (int)(lcm / right.Denominator) * right.Numerator, lcm);
  }

  public static Fract64 operator -(Fract64 value)
  {
    return new(-value.Numerator, value.Denominator);
  }

  public static Fract64 operator +(Fract64 value)
  {
    return value;
  }

  public static readonly Fract64 One = new(1, 1);
  public static readonly Fract64 Zero = new(0, 1);
  public static readonly Fract64 AdditiveIdentity = Zero;
  public static readonly Fract64 MultiplicativeIdentity = One;
  public static readonly int Radix = 2;

  static Fract64 INumberBase<Fract64>.One => One;
  static int INumberBase<Fract64>.Radix => Radix;
  static Fract64 INumberBase<Fract64>.Zero => Zero;
  static Fract64 IAdditiveIdentity<Fract64, Fract64>.AdditiveIdentity => AdditiveIdentity;
  static Fract64 IMultiplicativeIdentity<Fract64, Fract64>.MultiplicativeIdentity => MultiplicativeIdentity;
}