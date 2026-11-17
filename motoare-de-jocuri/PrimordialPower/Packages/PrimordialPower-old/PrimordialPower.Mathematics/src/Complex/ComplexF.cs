using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PrimordialPower.Mathematics.ConstArrays;

namespace PrimordialPower.Mathematics.Complex;

using ThisNumber = ComplexF;
using ThisValue = float;

[StructLayout(LayoutKind.Explicit)]
public struct ComplexF :
  IFloatingPointIeee754<ThisNumber>,
  IConvertible,
  IMinMaxValue<ThisNumber>
{
  [FieldOffset(0)]
  private ConstArray2<ThisValue> array;



  [FieldOffset(sizeof(ThisValue) * 0)]
  public ThisValue Real;

  [FieldOffset(sizeof(ThisValue) * 1)]
  public ThisValue Img;



  public ComplexF(ThisValue real, ThisValue imaginary)
  {
    this.Real = real;
    this.Img = imaginary;
  }

  public ComplexF(ThisValue real)
  {
    this.Real = real;
    this.Img = 0F;
  }



  public ThisNumber Conjugate
  => new(this.Real, -this.Img);

  public ThisNumber TimesI
  => new(-this.Img, this.Real);

  public ThisValue Argument
  => ThisValue.Atan2(Real, Img);

  public ThisValue Magnitude
  => ThisValue.Sqrt(MagnitudeSquared);

  public ThisValue MagnitudeSquared
  => Real * Real + Img * Img;



  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static implicit operator ThisNumber(ThisValue real)
  => new(real, 0);



  public static ThisNumber I
  => new(0F, 1F);

  public static ThisNumber Epsilon
  => ThisValue.Epsilon;

  public static ThisNumber NaN
  => new(ThisValue.NaN, ThisValue.NaN);

  public static ThisNumber NegativeInfinity
  => ThisValue.NegativeInfinity;

  public static ThisNumber NegativeZero
  => ThisValue.NegativeZero;

  public static ThisNumber PositiveInfinity
  => ThisValue.PositiveInfinity;

  public static ThisNumber NegativeOne
  => -1F;

  public static ThisNumber E
  => ThisValue.E;

  public static ThisNumber Pi
  => ThisValue.Pi;

  public static ThisNumber Tau
  => ThisValue.Tau;

  public static ThisNumber One
  => 1F;

  public static int Radix
  => 2;

  public static ThisNumber Zero
  => 0F;

  public static ThisNumber AdditiveIdentity
  => Zero;

  public static ThisNumber MultiplicativeIdentity
  => One;

  public static ThisNumber MaxValue
  => new(ThisValue.MaxValue, ThisValue.MaxValue);

  public static ThisNumber MinValue
  => new(ThisValue.MinValue, ThisValue.MinValue);

  public static ThisNumber Abs(ThisNumber value)
  => ThisValue.Sqrt((value.Real * value.Real) + (value.Img * value.Img));

  public static ThisNumber Acos(ThisNumber x)
  {
    ThisNumber sqrtPart = Sqrt(One - x * x);
    ThisNumber logArg = x + sqrtPart.TimesI; ;
    return new(-Log(logArg).Img, Log(logArg).Real);
  }

  public static ThisNumber Acosh(ThisNumber x)
  => Log(x + Sqrt(x - One) * Sqrt(x + One));

  public static ThisNumber AcosPi(ThisNumber x)
  => Acos(x) / Pi;

  public static ThisNumber Asin(ThisNumber x)
  {
    ThisNumber ix = x.TimesI;
    ThisNumber sqrtPart = Sqrt(One - x * x);
    return new(-Log(ix + sqrtPart).Img, Log(ix + sqrtPart).Real);
  }

  public static ThisNumber Asinh(ThisNumber x)
  => Log(x + Sqrt(x * x + One));

  public static ThisNumber AsinPi(ThisNumber x)
  => Asin(x) / Pi;

  public static ThisNumber Atan(ThisNumber x)
  {
    ThisNumber numerator = I + x;
    ThisNumber denominator = I - x;
    ThisNumber logPart = Log(numerator / denominator);
    return new(-logPart.Img / 2F, logPart.Real / 2F);
  }

  public static ThisNumber Atan2(ThisNumber y, ThisNumber x)
  {
    if (IsZero(x))
    {
      return NaN;
    }

    ThisNumber result = Atan(y / x);

    if (x.Real < 0F)
    {
      if (y.Real >= 0F)
        result = result + Pi;
      else
        result = result - Pi;
    }

    return result;
  }

  public static ThisNumber Atan2Pi(ThisNumber y, ThisNumber x)
  => new();

  public static ThisNumber Atanh(ThisNumber x)
  {
    ThisNumber numerator = One + x;
    ThisNumber denominator = One - x;
    ThisNumber logPart = Log(numerator / denominator);
    return new(logPart.Real / 2F, logPart.Img / 2F);
  }

  public static ThisNumber AtanPi(ThisNumber x)
  => Atan(x) / Pi;

  public static ThisNumber BitDecrement(ThisNumber x)
  => new(ThisValue.BitDecrement(x.Real), ThisValue.BitDecrement(x.Img));

  public static ThisNumber BitIncrement(ThisNumber x)
  => new(ThisValue.BitIncrement(x.Real), ThisValue.BitIncrement(x.Img));

  public static ThisNumber Cbrt(ThisNumber x)
  => RootN(x, 3);

  public static ThisNumber Cos(ThisNumber x)
  => new(
    ThisValue.Cos(x.Real) * ThisValue.Cosh(x.Img),
    -ThisValue.Sin(x.Real) * ThisValue.Sinh(x.Img)
  );

  public static ThisNumber Cosh(ThisNumber x)
  => new(
    ThisValue.Sin(x.Real) * ThisValue.Cosh(x.Img),
    ThisValue.Cos(x.Real) * ThisValue.Sinh(x.Img)
  );

  public static ThisNumber CosPi(ThisNumber x)
  => Cos(x * Pi);

  public static ThisNumber Exp(ThisNumber x)
  {
    var expReal = ThisValue.Exp(x.Real);
    return new(expReal * ThisValue.Cos(x.Img), expReal * ThisValue.Sin(x.Img));
  }

  public static ThisNumber Exp10(ThisNumber x)
  => Pow(10F, x);

  public static ThisNumber Exp2(ThisNumber x)
  => Pow(2F, x);

  public static ThisNumber FusedMultiplyAdd(ThisNumber left, ThisNumber right, ThisNumber addend)
  => (left * right) + addend;

  public static ThisNumber Hypot(ThisNumber x, ThisNumber y)
  {
    return Sqrt(x * x + y * y);
  }

  public static ThisNumber Ieee754Remainder(ThisNumber left, ThisNumber right)
  {
    var real = ThisValue.Ieee754Remainder(left.Real, right.Real);
    var img = ThisValue.Ieee754Remainder(left.Img, right.Img);
    return new(real, img);
  }

  public static int ILogB(ThisNumber x)
  => ThisValue.ILogB(x.Magnitude);

  public static bool IsCanonical(ThisNumber value)
  => true;

  public static bool IsComplexNumber(ThisNumber value)
  => ThisValue.IsFinite(value.Img) && value.Img != 0F;

  public static bool IsEvenInteger(ThisNumber value)
  => value.Img == 0F && ThisValue.IsEvenInteger(value.Real);

  public static bool IsFinite(ThisNumber value)
  => ThisValue.IsFinite(value.Real) && ThisValue.IsFinite(value.Img);

  public static bool IsImaginaryNumber(ThisNumber value)
  => ThisValue.IsFinite(value.Real) && value.Real == 0F && value.Img != 0F;

  public static bool IsInfinity(ThisNumber value)
  => ThisValue.IsInfinity(value.Real) || ThisValue.IsInfinity(value.Img);

  public static bool IsInteger(ThisNumber value)
  => value.Img == 0F && ThisValue.IsInteger(value.Real);

  public static bool IsNaN(ThisNumber value)
  => ThisValue.IsNaN(value.Real) || ThisValue.IsNaN(value.Img);

  public static bool IsNegative(ThisNumber value)
  => value.Real < 0F || value.Img < 0F;

  public static bool IsNegativeInfinity(ThisNumber value)
  => ThisValue.IsNegativeInfinity(value.Real) || ThisValue.IsNegativeInfinity(value.Img);

  public static bool IsNormal(ThisNumber value)
  => ThisValue.IsNormal(value.Real) && ThisValue.IsNormal(value.Img);

  public static bool IsOddInteger(ThisNumber value)
  => value.Img == 0F && ThisValue.IsOddInteger(value.Real);

  public static bool IsPositive(ThisNumber value)
  => value.Real > 0F || value.Img > 0F;

  public static bool IsPositiveInfinity(ThisNumber value)
  => ThisValue.IsPositiveInfinity(value.Real) || ThisValue.IsPositiveInfinity(value.Img);

  public static bool IsRealNumber(ThisNumber value)
  => ThisValue.IsFinite(value.Real) && value.Img == 0F;

  public static bool IsSubnormal(ThisNumber value)
  => ThisValue.IsSubnormal(value.Real) || ThisValue.IsSubnormal(value.Img);

  public static bool IsZero(ThisNumber value)
  => value.Real == 0F && value.Img == 0F;

  public static ThisNumber Log(ThisNumber x)
  {
    var magnitude = x.Magnitude;
    var argument = x.Argument;
    return new(ThisValue.Log(magnitude), argument);
  }

  public static ThisNumber Log(ThisNumber x, ThisNumber newBase)
  => Log(x) / Log(newBase);

  public static ThisNumber Log10(ThisNumber x)
  => Log(x) / Log(10F);

  public static ThisNumber Log2(ThisNumber x)
  => Log(x) / Log(2F);

  public static ThisNumber MaxMagnitude(ThisNumber x, ThisNumber y)
  => x.MagnitudeSquared >= y.MagnitudeSquared ? x : y;

  public static ThisNumber MaxMagnitudeNumber(ThisNumber x, ThisNumber y)
  {
    if (IsNaN(x)) return y;
    if (IsNaN(y)) return x;
    return MaxMagnitude(x, y);
  }

  public static ThisNumber MinMagnitude(ThisNumber x, ThisNumber y)
  => x.MagnitudeSquared <= y.MagnitudeSquared ? x : y;

  public static ThisNumber MinMagnitudeNumber(ThisNumber x, ThisNumber y)
  {
    if (IsNaN(x)) return y;
    if (IsNaN(y)) return x;
    return MinMagnitude(x, y);
  }

  public static ThisNumber Parse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider)
  {
    if (TryParse(s, style, provider, out var result))
      return result;
    throw new FormatException($"Unable to parse '{s.ToString()}' as ComplexF");
  }

  public static ThisNumber Parse(string s, NumberStyles style, IFormatProvider? provider)
  => Parse(s.AsSpan(), style, provider);

  public static ThisNumber Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
  => Parse(s, NumberStyles.Float | NumberStyles.AllowThousands, provider);

  public static ThisNumber Parse(string s, IFormatProvider? provider)
  => Parse(s.AsSpan(), provider);

  public static ThisNumber Pow(ThisNumber x, ThisNumber y)
  {
    if (IsZero(x))
    {
      if (IsZero(y)) return One;
      return Zero;
    }
    return Exp(y * Log(x));
  }

  public static ThisNumber RootN(ThisNumber x, int n)
  {
    if (n == 0) throw new ArgumentException("Cannot compute 0-th root", nameof(n));
    if (n == 1) return x;

    var magnitude = ThisValue.Pow(x.Magnitude, 1F / n);
    var argument = x.Argument / n;
    return new(magnitude * ThisValue.Cos(argument), magnitude * ThisValue.Sin(argument));
  }

  public static ThisNumber Round(ThisNumber x, int digits, MidpointRounding mode)
  => new(ThisValue.Round(x.Real, digits, mode), ThisValue.Round(x.Img, digits, mode));

  public static ThisNumber ScaleB(ThisNumber x, int n)
  => new(ThisValue.ScaleB(x.Real, n), ThisValue.ScaleB(x.Img, n));

  public static ThisNumber Sin(ThisNumber x)
  => new(
    ThisValue.Sin(x.Real) * ThisValue.Cosh(x.Img),
    ThisValue.Cos(x.Real) * ThisValue.Sinh(x.Img)
  );

  public static (ThisNumber Sin, ThisNumber Cos) SinCos(ThisNumber x)
  => (Sin(x), Cos(x));

  public static (ThisNumber SinPi, ThisNumber CosPi) SinCosPi(ThisNumber x)
  => (SinPi(x), CosPi(x));

  public static ThisNumber Sinh(ThisNumber x)
  => new(
    ThisValue.Sinh(x.Real) * ThisValue.Cos(x.Img),
    ThisValue.Cosh(x.Real) * ThisValue.Sin(x.Img)
  );

  public static ThisNumber SinPi(ThisNumber x)
  => Sin(x * Pi);

  public static ThisNumber Sqrt(ThisNumber x)
  => RootN(x, 2);

  public static ThisNumber Tan(ThisNumber x)
  => Sin(x) / Cos(x);

  public static ThisNumber Tanh(ThisNumber x)
  => Sinh(x) / Cosh(x);

  public static ThisNumber TanPi(ThisNumber x)
  => Tan(x * Pi);

  public static bool TryConvertFromChecked<TOther>(TOther value, [MaybeNullWhen(false)] out ThisNumber result) where TOther : INumberBase<TOther>
  {
    if (typeof(TOther) == typeof(ThisNumber))
    {
      result = (ThisNumber)(object)value;
      return true;
    }

    if (TOther.TryConvertToChecked(value, out ThisValue realValue))
    {
      result = realValue;
      return true;
    }

    result = default;
    return false;
  }

  public static bool TryConvertFromSaturating<TOther>(TOther value, [MaybeNullWhen(false)] out ThisNumber result) where TOther : INumberBase<TOther>
  {
    if (typeof(TOther) == typeof(ThisNumber))
    {
      result = (ThisNumber)(object)value;
      return true;
    }

    if (TOther.TryConvertToSaturating(value, out ThisValue realValue))
    {
      result = realValue;
      return true;
    }

    result = default;
    return false;
  }

  public static bool TryConvertFromTruncating<TOther>(TOther value, [MaybeNullWhen(false)] out ThisNumber result) where TOther : INumberBase<TOther>
  {
    if (typeof(TOther) == typeof(ThisNumber))
    {
      result = (ThisNumber)(object)value;
      return true;
    }

    if (TOther.TryConvertToTruncating(value, out ThisValue realValue))
    {
      result = realValue;
      return true;
    }

    result = default;
    return false;
  }

  public static bool TryConvertToChecked<TOther>(ThisNumber value, [MaybeNullWhen(false)] out TOther result) where TOther : INumberBase<TOther>
  {
    if (typeof(TOther) == typeof(ThisNumber))
    {
      result = (TOther)(object)value;
      return true;
    }

    return TOther.TryConvertFromChecked(value.Real, out result);
  }

  public static bool TryConvertToSaturating<TOther>(ThisNumber value, [MaybeNullWhen(false)] out TOther result) where TOther : INumberBase<TOther>
  {
    if (typeof(TOther) == typeof(ThisNumber))
    {
      result = (TOther)(object)value;
      return true;
    }

    return TOther.TryConvertFromSaturating(value.Real, out result);
  }

  public static bool TryConvertToTruncating<TOther>(ThisNumber value, [MaybeNullWhen(false)] out TOther result) where TOther : INumberBase<TOther>
  {
    if (typeof(TOther) == typeof(ThisNumber))
    {
      result = (TOther)(object)value;
      return true;
    }

    return TOther.TryConvertFromTruncating(value.Real, out result);
  }

  public static bool TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, [MaybeNullWhen(false)] out ThisNumber result)
  {
    // Simple parser for formats: "real", "(real, imgi)", "real+imgi", "real-imgi"
    result = default;
    s = s.Trim();

    // Check for parentheses format: "(real, imgi)"
    if (s.Length > 2 && s[0] == '(' && s[^1] == ')')
    {
      s = s.Slice(1, s.Length - 2);
      var commaIdx = s.IndexOf(',');
      if (commaIdx >= 0)
      {
        var realPart = s.Slice(0, commaIdx).Trim();
        var imgPart = s.Slice(commaIdx + 1).Trim();

        // Remove 'i' from imaginary part
        if (imgPart.Length > 0 && imgPart[^1] == 'i')
          imgPart = imgPart.Slice(0, imgPart.Length - 1).Trim();

        if (ThisValue.TryParse(realPart, style, provider, out var real) &&
            ThisValue.TryParse(imgPart, style, provider, out var img))
        {
          result = new(real, img);
          return true;
        }
      }
    }

    // Try simple real number
    if (ThisValue.TryParse(s, style, provider, out var realValue))
    {
      result = realValue;
      return true;
    }

    return false;
  }

  public static bool TryParse([NotNullWhen(true)] string? s, NumberStyles style, IFormatProvider? provider, [MaybeNullWhen(false)] out ThisNumber result)
  {
    if (s == null)
    {
      result = default;
      return false;
    }
    return TryParse(s.AsSpan(), style, provider, out result);
  }

  public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, [MaybeNullWhen(false)] out ThisNumber result)
  => TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, provider, out result);

  public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out ThisNumber result)
  => TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, provider, out result);

  public int CompareTo(object? obj)
  {
    if (obj is null) return 1;
    if (obj is ThisNumber other) return CompareTo(other);
    throw new ArgumentException("Object is not a ComplexF");
  }

  public int CompareTo(ThisNumber other)
  {
    ThisValue thisMag = this.MagnitudeSquared;
    ThisValue otherMag = other.MagnitudeSquared;
    return thisMag.CompareTo(otherMag);
  }

  public bool Equals(ThisNumber other)
  => Real == other.Real && Img == other.Img;

  public override bool Equals(object? obj)
  => obj is ThisNumber other && Equals(other);

  public override int GetHashCode()
  => HashCode.Combine(Real, Img);

  public int GetExponentByteCount()
  => sizeof(ThisValue);

  public int GetExponentShortestBitLength()
  => ThisValue.ILogB(Magnitude) + 1;

  public int GetSignificandBitLength()
  => sizeof(ThisValue) * 8 * 2;

  public int GetSignificandByteCount()
  => sizeof(ThisValue) * 2;

  public string ToString(string? format, IFormatProvider? formatProvider)
  => $"({Real.ToString(format, formatProvider)}, {Img.ToString(format, formatProvider)}i)";

  public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
  {
    string str = ToString(format.ToString(), provider);
    if (str.Length > destination.Length)
    {
      charsWritten = 0;
      return false;
    }
    str.AsSpan().CopyTo(destination);
    charsWritten = str.Length;
    return true;
  }

  public bool TryWriteExponentBigEndian(Span<byte> destination, out int bytesWritten)
  {
    if (destination.Length < sizeof(short))
    {
      bytesWritten = 0;
      return false;
    }

    var exponent = (short)ThisValue.ILogB(Magnitude);
    if (BitConverter.IsLittleEndian)
    {
      var bytes = BitConverter.GetBytes(exponent);
      destination[0] = bytes[1];
      destination[1] = bytes[0];
    }
    else
    {
      BitConverter.TryWriteBytes(destination, exponent);
    }

    bytesWritten = sizeof(short);
    return true;
  }

  public bool TryWriteExponentLittleEndian(Span<byte> destination, out int bytesWritten)
  {
    if (destination.Length < sizeof(short))
    {
      bytesWritten = 0;
      return false;
    }

    var exponent = (short)ThisValue.ILogB(Magnitude);
    if (!BitConverter.IsLittleEndian)
    {
      var bytes = BitConverter.GetBytes(exponent);
      destination[0] = bytes[1];
      destination[1] = bytes[0];
    }
    else
    {
      BitConverter.TryWriteBytes(destination, exponent);
    }

    bytesWritten = sizeof(short);
    return true;
  }

  public bool TryWriteSignificandBigEndian(Span<byte> destination, out int bytesWritten)
  {
    if (destination.Length < sizeof(ThisValue) * 2)
    {
      bytesWritten = 0;
      return false;
    }

    var realBytes = BitConverter.GetBytes(Real);
    var imgBytes = BitConverter.GetBytes(Img);

    if (BitConverter.IsLittleEndian)
    {
      // Reverse for big endian
      for (int i = 0; i < sizeof(ThisValue); i++)
      {
        destination[i] = realBytes[sizeof(ThisValue) - 1 - i];
        destination[sizeof(ThisValue) + i] = imgBytes[sizeof(ThisValue) - 1 - i];
      }
    }
    else
    {
      realBytes.CopyTo(destination);
      imgBytes.CopyTo(destination.Slice(sizeof(ThisValue)));
    }

    bytesWritten = sizeof(ThisValue) * 2;
    return true;
  }

  public bool TryWriteSignificandLittleEndian(Span<byte> destination, out int bytesWritten)
  {
    if (destination.Length < sizeof(ThisValue) * 2)
    {
      bytesWritten = 0;
      return false;
    }

    var realBytes = BitConverter.GetBytes(Real);
    var imgBytes = BitConverter.GetBytes(Img);

    if (!BitConverter.IsLittleEndian)
    {
      for (int i = 0; i < sizeof(ThisValue); i++)
      {
        destination[i] = realBytes[sizeof(ThisValue) - 1 - i];
        destination[sizeof(ThisValue) + i] = imgBytes[sizeof(ThisValue) - 1 - i];
      }
    }
    else
    {
      realBytes.CopyTo(destination);
      imgBytes.CopyTo(destination.Slice(sizeof(ThisValue)));
    }

    bytesWritten = sizeof(ThisValue) * 2;
    return true;
  }

  public TypeCode GetTypeCode()
  => TypeCode.Object;

  public bool ToBoolean(IFormatProvider? provider)
  => !IsZero(this);

  public byte ToByte(IFormatProvider? provider)
  => (byte)Real;

  public char ToChar(IFormatProvider? provider)
  => (char)Real;

  public DateTime ToDateTime(IFormatProvider? provider)
  => throw new InvalidCastException("Cannot convert ComplexF to DateTime");

  public decimal ToDecimal(IFormatProvider? provider)
  => (decimal)Real;

  public double ToDouble(IFormatProvider? provider)
  => Real;

  public short ToInt16(IFormatProvider? provider)
  => (short)Real;

  public int ToInt32(IFormatProvider? provider)
  => (int)Real;

  public long ToInt64(IFormatProvider? provider)
  => (long)Real;

  public sbyte ToSByte(IFormatProvider? provider)
  => (sbyte)Real;

  public float ToSingle(IFormatProvider? provider)
  => (float)Real;

  public string ToString(IFormatProvider? provider)
  => $"({Real}, {Img}i)";

  public object ToType(Type conversionType, IFormatProvider? provider)
  => Convert.ChangeType(Real, conversionType, provider);

  public ushort ToUInt16(IFormatProvider? provider)
  => (ushort)Real;

  public uint ToUInt32(IFormatProvider? provider)
  => (uint)Real;

  public ulong ToUInt64(IFormatProvider? provider)
  => (ulong)Real;

  public static bool IsPow2(ThisNumber value)
  => value.Img == 0F && ThisValue.IsPow2(value.Real);

  public static ThisNumber operator +(ThisNumber value)
  => value;

  public static ThisNumber operator +(ThisNumber left, ThisNumber right)
  => new(left.Real + right.Real, left.Img + right.Img);

  public static ThisNumber operator -(ThisNumber value)
  => new(-value.Real, -value.Img);

  public static ThisNumber operator -(ThisNumber left, ThisNumber right)
  => new(left.Real - right.Real, left.Img - right.Img);

  public static ThisNumber operator ++(ThisNumber value)
  => value + One;

  public static ThisNumber operator --(ThisNumber value)
  => value - One;

  public static ThisNumber operator *(ThisNumber left, ThisNumber right)
  {
    // (a + bi)(c + di) = (ac - bd) + (ad + bc)i
    var real = left.Real * right.Real - left.Img * right.Img;
    var img = left.Real * right.Img + left.Img * right.Real;
    return new(real, img);
  }

  public static ThisNumber operator /(ThisNumber left, ThisNumber right)
  {
    // (a + bi)/(c + di) = [(ac + bd) + (bc - ad)i] / (c² + d²)
    var denominator = right.Real * right.Real + right.Img * right.Img;
    var real = (left.Real * right.Real + left.Img * right.Img) / denominator;
    var img = (left.Img * right.Real - left.Real * right.Img) / denominator;
    return new(real, img);
  }

  public static ThisNumber operator %(ThisNumber left, ThisNumber right)
  {
    // Complex modulo: left - (floor(left/right) * right)
    var division = left / right;
    var floored = new ThisNumber(ThisValue.Floor(division.Real), ThisValue.Floor(division.Img));
    return left - (floored * right);
  }

  public static bool operator ==(ThisNumber left, ThisNumber right)
  => left.Equals(right);

  public static bool operator !=(ThisNumber left, ThisNumber right)
  => !left.Equals(right);

  public static bool operator <(ThisNumber left, ThisNumber right)
  => left.MagnitudeSquared < right.MagnitudeSquared;

  public static bool operator >(ThisNumber left, ThisNumber right)
  => left.MagnitudeSquared > right.MagnitudeSquared;

  public static bool operator <=(ThisNumber left, ThisNumber right)
  => left.MagnitudeSquared <= right.MagnitudeSquared;

  public static bool operator >=(ThisNumber left, ThisNumber right)
  => left.MagnitudeSquared >= right.MagnitudeSquared;
}