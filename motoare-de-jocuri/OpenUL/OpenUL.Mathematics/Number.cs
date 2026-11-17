using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace OpenUL.Mathematics;

public static class Number
{
  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static T Abs<T>(T value) where T : INumberBase<T> => T.Abs(value);
  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static int Sign<T>(T value) where T : INumber<T> => T.Sign(value);
  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static T CopySign<T>(T value, T sign) where T : INumber<T> => T.CopySign(value, sign);
  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static T Clamp<T>(T value, T min, T max) where T : INumber<T> => T.Clamp(value, min, max);


  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static T Max<T>(T x, T y) where T : INumber<T> => T.Max(x, y);
  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static T MaxNumber<T>(T x, T y) where T : INumber<T> => T.MaxNumber(x, y);
  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static T MaxMagnitude<T>(T x, T y) where T : INumberBase<T> => T.MaxMagnitude(x, y);
  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static T MaxMagnitudeNumber<T>(T x, T y) where T : INumberBase<T> => T.MaxMagnitudeNumber(x, y);
  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static T Min<T>(T x, T y) where T : INumber<T> => T.Min(x, y);
  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static T MinNumber<T>(T x, T y) where T : INumber<T> => T.MinNumber(x, y);
  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static T MinMagnitude<T>(T x, T y) where T : INumberBase<T> => T.MinMagnitude(x, y);
  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static T MinMagnitudeNumber<T>(T x, T y) where T : INumberBase<T> => T.MinMagnitudeNumber(x, y);


  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static bool IsCanonical<T>(T value) where T : INumberBase<T> => T.IsCanonical(value);
  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static bool IsComplexNumber<T>(T value) where T : INumberBase<T> => T.IsComplexNumber(value);
  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static bool IsEvenInteger<T>(T value) where T : INumberBase<T> => T.IsEvenInteger(value);
  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static bool IsFinite<T>(T value) where T : INumberBase<T> => T.IsFinite(value);
  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static bool IsImaginaryNumber<T>(T value) where T : INumberBase<T> => T.IsImaginaryNumber(value);
  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static bool IsInfinity<T>(T value) where T : INumberBase<T> => T.IsInfinity(value);
  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static bool IsInteger<T>(T value) where T : INumberBase<T> => T.IsInteger(value);
  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static bool IsNaN<T>(T value) where T : INumberBase<T> => T.IsNaN(value);
  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static bool IsNegative<T>(T value) where T : INumberBase<T> => T.IsNegative(value);
  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static bool IsNegativeInfinity<T>(T value) where T : INumberBase<T> => T.IsNegativeInfinity(value);
  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static bool IsNormal<T>(T value) where T : INumberBase<T> => T.IsNormal(value);
  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static bool IsOddInteger<T>(T value) where T : INumberBase<T> => T.IsOddInteger(value);
  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static bool IsPositive<T>(T value) where T : INumberBase<T> => T.IsPositive(value);
  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static bool IsPositiveInfinity<T>(T value) where T : INumberBase<T> => T.IsPositiveInfinity(value);
  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static bool IsRealNumber<T>(T value) where T : INumberBase<T> => T.IsRealNumber(value);
  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static bool IsSubnormal<T>(T value) where T : INumberBase<T> => T.IsSubnormal(value);
  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static bool IsZero<T>(T value) where T : INumberBase<T> => T.IsZero(value);


  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static TTo ConvertChecked<TFrom, TTo>(TFrom value)
    where TFrom : INumberBase<TFrom>
    where TTo : INumberBase<TTo>
    => TTo.CreateChecked(value);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static TTo ConvertSaturating<TFrom, TTo>(TFrom value)
    where TFrom : INumberBase<TFrom>
    where TTo : INumberBase<TTo>
    => TTo.CreateSaturating(value);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static TTo ConvertTruncating<TFrom, TTo>(TFrom value)
    where TFrom : INumberBase<TFrom>
    where TTo : INumberBase<TTo>
    => TTo.CreateTruncating(value);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static TTo Convert<TFrom, TTo>(TFrom value, NumberCreation creation = NumberCreation.Saturate)
    where TFrom : INumberBase<TFrom>
    where TTo : INumberBase<TTo>
    => creation switch
    {
      NumberCreation.Saturate => TTo.CreateSaturating(value),
      NumberCreation.Truncate => TTo.CreateTruncating(value),
      NumberCreation.Check => TTo.CreateChecked(value),
      _ => throw new ArgumentOutOfRangeException(),
    };


  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static T Parse<T>(string s, IFormatProvider? provider) where T : INumberBase<T>
    => T.Parse(s, provider);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static T Parse<T>(string s, NumberStyles style, IFormatProvider? provider) where T : INumberBase<T>
    => T.Parse(s, style, provider);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static T Parse<T>(ReadOnlySpan<char> s, IFormatProvider? provider) where T : INumberBase<T>
    => T.Parse(s, provider);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static T Parse<T>(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider) where T : INumberBase<T>
    => T.Parse(s, style, provider);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static bool TryParse<T>([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out T result)
    where T : INumberBase<T>
    => T.TryParse(s, provider, out result);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static bool TryParse<T>(string? s, NumberStyles style, IFormatProvider? provider, [MaybeNullWhen(false)] out T result)
    where T : INumberBase<T>
    => T.TryParse(s, style, provider, out result);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static bool TryParse<T>(ReadOnlySpan<char> s, IFormatProvider? provider, [MaybeNullWhen(false)] out T result)
    where T : INumberBase<T>
    => T.TryParse(s, provider, out result);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static bool TryParse<T>(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, [MaybeNullWhen(false)] out T result)
    where T : INumberBase<T>
    => T.TryParse(s, style, provider, out result);
}