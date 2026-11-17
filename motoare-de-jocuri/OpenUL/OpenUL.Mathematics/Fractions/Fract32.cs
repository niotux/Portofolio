using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;

namespace OpenUL.Mathematics.Fractions;

public readonly struct Fract32 : INumber<Fract32>
{
  public readonly short Numerator;
  public readonly ushort Denominator;

  public Fract32(short numerator, short denominator)
  {
    if (denominator == 0)
    {
      throw new DivideByZeroException("Denominator cannot be zero");
    }
    if (denominator < 0)
    {
      Numerator = (short)-numerator;
      Denominator = (ushort)-denominator;
    }
    else
    {
      Numerator = numerator;
      Denominator = (ushort)denominator;
    }
  }

  public Fract32(short numerator, ushort denominator)
  {
    if (denominator == 0)
    {
      throw new DivideByZeroException("Denominator cannot be zero");
    }
    Numerator = numerator;
    Denominator = denominator;
  }

  public Fract32 Reduce()
  {
    // Avoid overflow on int.MinValue by widening to long before taking the absolute value.
    ushort absNumerator = (ushort)Number.Abs(Numerator);
    ushort gcd = absNumerator == 0
      ? Denominator
      : Algorithm.GreatestCommonDenominator(absNumerator, Denominator);
    if (gcd == 0)
    {
      gcd = 1;
    }
    return new((short)(Numerator / gcd), (ushort)(Denominator / gcd));
  }

  public double ToDouble() => (double)Numerator / Denominator;
  public float ToSingle() => (float)Numerator / Denominator;

  public int CompareTo(object? obj)
  {
    if (obj is null)
    {
      return 1;
    }
    if (obj is Fract32 other)
    {
      return CompareTo(other);
    }
    throw new ArgumentException($"Object must be of type {nameof(Fract32)}", nameof(obj));
  }

  public int CompareTo(Fract32 other)
  {
    if (Denominator == other.Denominator)
    {
      return Numerator.CompareTo(other.Numerator);
    }

    long lhs = (long)Numerator * other.Denominator;
    long rhs = (long)other.Numerator * Denominator;
    return lhs.CompareTo(rhs);
  }

  public static Fract32 Abs(Fract32 value)
    => new(checked(Number.Abs(value.Numerator)), value.Denominator);

  public static bool IsCanonical(Fract32 value)
  {
    if (value.Numerator == 0)
    {
      return value.Denominator == 1;
    }
    uint absNumerator = (uint)Number.Abs(value.Numerator);
    uint gcd = Algorithm.GreatestCommonDenominator(absNumerator, value.Denominator);
    return gcd == 1;
  }

  public static bool IsComplexNumber(Fract32 value) => false;

  public static bool IsEvenInteger(Fract32 value)
  {
    var reduced = value.Reduce();
    return reduced.Denominator == 1 && reduced.Numerator % 2 == 0;
  }

  public static bool IsFinite(Fract32 value) => true;

  public static bool IsImaginaryNumber(Fract32 value) => false;

  public static bool IsInfinity(Fract32 value) => false;

  public static bool IsInteger(Fract32 value) => value.Reduce().Denominator == 1;

  public static bool IsNaN(Fract32 value) => false;

  public static bool IsNegative(Fract32 value) => value.Numerator < 0;

  public static bool IsNegativeInfinity(Fract32 value) => false;

  public static bool IsNormal(Fract32 value) => value.Numerator != 0;

  public static bool IsOddInteger(Fract32 value)
  {
    var reduced = value.Reduce();
    return reduced.Denominator == 1 && reduced.Numerator % 2 != 0;
  }

  public static bool IsPositive(Fract32 value) => value.Numerator >= 0;

  public static bool IsPositiveInfinity(Fract32 value) => false;

  public static bool IsRealNumber(Fract32 value) => true;

  public static bool IsSubnormal(Fract32 value) => false;

  public static bool IsZero(Fract32 value) => value.Numerator == 0;

  public static Fract32 MaxMagnitude(Fract32 x, Fract32 y)
  {
    Fract32 ax = Abs(x);
    Fract32 ay = Abs(y);
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

  public static Fract32 MaxMagnitudeNumber(Fract32 x, Fract32 y) => MaxMagnitude(x, y);

  public static Fract32 MinMagnitude(Fract32 x, Fract32 y)
  {
    Fract32 ax = Abs(x);
    Fract32 ay = Abs(y);
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

  public static Fract32 MinMagnitudeNumber(Fract32 x, Fract32 y) => MinMagnitude(x, y);

  public static Fract32 Parse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider)
  {
    if (TryParse(s, style, provider, out var result))
    {
      return result;
    }
    throw new FormatException($"Could not parse '{s.ToString()}' as a {nameof(Fract32)}");
  }

  public static Fract32 Parse(string s, NumberStyles style, IFormatProvider? provider)
  {
    ArgumentNullException.ThrowIfNull(s);
    return Parse(s.AsSpan(), style, provider);
  }

  public static bool TryConvertFromChecked<TOther>(TOther value, [MaybeNullWhen(false)] out Fract32 result) where TOther : INumberBase<TOther>
  {
    try
    {
      var numerator = short.CreateChecked(value);
      result = new Fract32(numerator, 1);
      return true;
    }
    catch (Exception ex) when (ex is OverflowException or NotSupportedException or FormatException)
    {
      result = default;
      return false;
    }
  }

  public static bool TryConvertFromSaturating<TOther>(TOther value, [MaybeNullWhen(false)] out Fract32 result) where TOther : INumberBase<TOther>
  {
    try
    {
      var numerator = short.CreateSaturating(value);
      result = new Fract32(numerator, 1);
      return true;
    }
    catch (Exception ex) when (ex is NotSupportedException or FormatException)
    {
      result = default;
      return false;
    }
  }

  public static bool TryConvertFromTruncating<TOther>(TOther value, [MaybeNullWhen(false)] out Fract32 result) where TOther : INumberBase<TOther>
  {
    try
    {
      var numerator = short.CreateTruncating(value);
      result = new Fract32(numerator, 1);
      return true;
    }
    catch (Exception ex) when (ex is NotSupportedException or FormatException)
    {
      result = default;
      return false;
    }
  }

  public static bool TryConvertToChecked<TOther>(Fract32 value, [MaybeNullWhen(false)] out TOther result) where TOther : INumberBase<TOther>
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

  public static bool TryConvertToSaturating<TOther>(Fract32 value, [MaybeNullWhen(false)] out TOther result) where TOther : INumberBase<TOther>
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

  public static bool TryConvertToTruncating<TOther>(Fract32 value, [MaybeNullWhen(false)] out TOther result) where TOther : INumberBase<TOther>
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

  public static bool TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, [MaybeNullWhen(false)] out Fract32 result)
  {
    int slashIndex = s.IndexOf('/');
    if (slashIndex >= 0)
    {
      var numeratorSpan = s[..slashIndex];
      var denominatorSpan = s[(slashIndex + 1)..];
      if (
        short.TryParse(numeratorSpan, style, provider, out var numerator) &&
        short.TryParse(denominatorSpan, style, provider, out var denominator) &&
        denominator != 0
      )
      {
        result = new Fract32(numerator, denominator);
        return true;
      }

      result = default;
      return false;
    }

    if (short.TryParse(s, style, provider, out var wholeValue))
    {
      result = new Fract32(wholeValue, 1);
      return true;
    }

    result = default;
    return false;
  }

  public static bool TryParse([NotNullWhen(true)] string? s, NumberStyles style, IFormatProvider? provider, [MaybeNullWhen(false)] out Fract32 result)
  {
    if (s is null)
    {
      result = default;
      return false;
    }
    return TryParse(s.AsSpan(), style, provider, out result);
  }

  public bool Equals(Fract32 other)
  {
    if (Denominator == other.Denominator)
    {
      return Numerator == other.Numerator;
    }
    return (long)Numerator * other.Denominator == (long)other.Numerator * Denominator;
  }

  public override bool Equals(object? obj) => obj is Fract32 other && Equals(other);

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

  public static Fract32 Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
    => Parse(s, NumberStyles.Integer, provider);

  public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, [MaybeNullWhen(false)] out Fract32 result)
    => TryParse(s, NumberStyles.Integer, provider, out result);

  public static Fract32 Parse(string s, IFormatProvider? provider)
    => Parse(s, NumberStyles.Integer, provider);

  public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out Fract32 result)
    => TryParse(s, NumberStyles.Integer, provider, out result);

  public static implicit operator Fract32(short num)
    => new(num, 1);

  public static bool operator >(Fract32 left, Fract32 right) => left.CompareTo(right) > 0;

  public static bool operator >=(Fract32 left, Fract32 right) => left.CompareTo(right) >= 0;

  public static bool operator <(Fract32 left, Fract32 right) => left.CompareTo(right) < 0;

  public static bool operator <=(Fract32 left, Fract32 right) => left.CompareTo(right) <= 0;

  public static Fract32 operator %(Fract32 left, Fract32 right)
  {
    if (left.Denominator == right.Denominator)
    {
      return new((short)(left.Numerator % right.Numerator), left.Denominator);
    }

    ushort lcm = Algorithm.LeastCommonMultiple(left.Denominator, right.Denominator);
    return new((short)(lcm / left.Denominator * left.Numerator % (lcm / right.Denominator) * right.Numerator), lcm);
  }

  public static Fract32 operator +(Fract32 left, Fract32 right)
  {
    if (left.Denominator == right.Denominator)
    {
      return new((short)(left.Numerator + right.Numerator), left.Denominator);
    }

    ushort lcm = Algorithm.LeastCommonMultiple(left.Denominator, right.Denominator);
    return new((short)(lcm / left.Denominator * left.Numerator + lcm / right.Denominator * right.Numerator), lcm);
  }

  public static Fract32 operator --(Fract32 value)
    => new((short)(value.Numerator - value.Denominator), value.Denominator);

  public static Fract32 operator /(Fract32 left, Fract32 right)
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

    return new((short)num, (ushort)den);
  }

  public static bool operator ==(Fract32 left, Fract32 right) => left.Equals(right);

  public static bool operator !=(Fract32 left, Fract32 right) => !left.Equals(right);

  public static Fract32 operator ++(Fract32 value)
    => new((short)(value.Numerator + value.Denominator), value.Denominator);

  public static Fract32 operator *(Fract32 left, Fract32 right)
  {
    long num = (long)left.Numerator * right.Numerator;
    long den = (long)left.Denominator * right.Denominator;
    return new((short)num, (ushort)den);
  }

  public static Fract32 operator -(Fract32 left, Fract32 right)
  {
    if (left.Denominator == right.Denominator)
    {
      return new((short)(left.Numerator - right.Numerator), left.Denominator);
    }

    ushort lcm = Algorithm.LeastCommonMultiple(left.Denominator, right.Denominator);
    return new((short)((int)(lcm / left.Denominator) * left.Numerator - lcm / right.Denominator * right.Numerator), lcm);
  }

  public static Fract32 operator -(Fract32 value)
  {
    return new((short)-value.Numerator, value.Denominator);
  }

  public static Fract32 operator +(Fract32 value)
  {
    return value;
  }

  public static readonly Fract32 One = new(1, 1);
  public static readonly Fract32 Zero = new(0, 1);
  public static readonly Fract32 AdditiveIdentity = Zero;
  public static readonly Fract32 MultiplicativeIdentity = One;
  public static readonly int Radix = 2;

  static Fract32 INumberBase<Fract32>.One => One;
  static int INumberBase<Fract32>.Radix => Radix;
  static Fract32 INumberBase<Fract32>.Zero => Zero;
  static Fract32 IAdditiveIdentity<Fract32, Fract32>.AdditiveIdentity => AdditiveIdentity;
  static Fract32 IMultiplicativeIdentity<Fract32, Fract32>.MultiplicativeIdentity => MultiplicativeIdentity;
}