using System.Text;
using System.Globalization;
using LAN.Logging;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Buffers.Binary;
using System.Runtime.Serialization.Formatters.Binary;
using System.Net.Http.Headers;

namespace LAN.Assembler.Pipeline2;

public static class Symbolizer
{
  // ---- Public helpers ----

  public static Symbol? GetValue(Token token, ulong typeSize, Logger? logger)
  {
    var view = token.View;

    return token.Kind switch
    {
      TokenKind.String => MakeString(view, typeSize, logger),
      TokenKind.Number => MakeNumber(view, typeSize, logger),
      TokenKind.Identifier => MakeValueFromIdentifier(view, logger),
      _ => null,
    };
  }

  public static Symbol? MakeValueFromIdentifier(SourceView view, Logger? logger)
  {
    if (Keywords.TryGetValueKeyword(view, out ulong buffer))
    {
      return MakeLiteralBuffer(buffer, view.Line, view.Character);
    }
    return MakeIdentifier(view);
  }

  public static Symbol? GetTypeSize(Token token, Logger? logger)
  {
    var view = token.View;

    if (token.Kind is TokenKind.String or TokenKind.Symbol)
    {
      logger?.Log(SymbolizerMessages.InvalidTypeSize, view);
      return null;
    }

    if (view == Keywords.Ptr && token.Kind == TokenKind.Identifier)
    {
      return MakeLiteralBuffer(ulong.MaxValue, view.Line, view.Character);
    }

    return token.Kind switch
    {
      TokenKind.Number => MakeInteger(view, sizeof(ulong), null, logger),
      TokenKind.Identifier => MakeIdentifier(view),
      _ => null,
    };
  }

  [StructLayout(LayoutKind.Explicit)]
  private struct DoubleInt64Union
  {
    [FieldOffset(0)]
    public double Double;
    [FieldOffset(0)]
    public ulong UInt64;
  }

  public static Symbol? MakeNumber(SourceView view, ulong typeSize, Logger? logger)
  {
    var span = view.Span;
    bool neg = span[0] == Grammar.NegativeSign;
    uint offset = neg
      ? 1u
      : 0u;

    var radix = Keywords.GetRadix(span.Slice((int)offset));
    if (radix == null)
    {
      if (IsFloatShaped(span))
      {
        return MakeFloat(view, logger);
      }

      logger?.Log(SymbolizerMessages.InvalidIntegerLiteral, view.Trim(offset));
      return null;
    }

    return MakeInteger(view, typeSize, radix.Value, logger);
  }

  // ---- Symbol factories (GetTypeSize path — full precision) ----

  private static Symbol? MakeFloat(SourceView view, Logger? logger)
  {
    DoubleInt64Union doubleULong = default;
    if (double.TryParse(view.Span, NumberStyles.Float, CultureInfo.InvariantCulture, out doubleULong.Double))
    {
      return MakeLiteralBuffer(doubleULong.UInt64, view.Line, view.Character);
    }

    logger?.Log(SymbolizerMessages.InvalidFloatLiteral, view);
    return null;
  }

  // ---- Parsing helpers (shared) ----

  // A radix-prefixed number (0x/0b/0o) is never a float; otherwise a '.', 'e' or 'E'
  // marks the decimal literal as floating point.
  private static bool IsFloatShaped(ReadOnlySpan<char> span)
  {
    int i = span[0] == Grammar.NegativeSign
      ? 1
      : 0;

    char exponentUpper = char.ToUpperInvariant(Grammar.Exponent);
    while (i < span.Length)
    {
      char c = span[i];
      if (c == Grammar.DecimalPoint || c == Grammar.Exponent || c == exponentUpper)
      {
        return true;
      }
      i++;
    }
    return false;
  }

  // Fast path: accumulate into a ulong (Buffer, no allocation). Only on 64-bit
  // overflow does the wide path allocate the little-endian Bytes representation.
  private static Symbol? MakeInteger(SourceView view, ulong typeSize, uint? radix, Logger? logger)
  {
    bool negative = view[0] == Grammar.NegativeSign;
    uint start = negative ? 1u : 0u;

    var sign = view.First(1);
    var contents = view.Trim(start);

    if (view.Length - start == 0)
    {
      logger?.Log(SymbolizerMessages.ExpectedDecimalIntegerAfterMinus, sign);
      return null;
    }

    if (radix is null)
    {
      radix = Keywords.GetRadix(contents.Span);
      if (radix == null)
      {
        logger?.Log(SymbolizerMessages.InvalidIntegerLiteral, contents);
        return null;
      }
    }

    ulong acc = 0;
    bool overflow = false;
    for (uint i = start; i < view.Length; i++)
    {
      int digit = DigitValue(view[i], radix.Value);
      if (digit < 0)
      {
        logger?.Log(SymbolizerMessages.InvalidIntegerLiteral, contents);
        return null;
      }
      if (acc > (ulong.MaxValue - (ulong)digit) / radix)
      {
        overflow = true;
        break;
      }
      acc = acc * (ulong)radix + (ulong)digit;
    }

    if (!overflow && !(negative && acc > (1UL << 63)))
    {
      ulong buffer = negative ? unchecked(0UL - acc) : acc;
      if (typeSize < 8 && buffer >= (1ul << ((int)typeSize * 8)))
      {
        logger?.Log(SymbolizerMessages.LiteralTooLong, view);
      }
      return MakeLiteralBuffer(buffer, view.Line, view.Character);
    }

    return MakeWideInteger(view, typeSize, radix.Value, negative, logger);
  }

  private static int DigitValue(char c, uint radix)
  {
    int value;
    if (c >= '0' && c <= '9')
    {
      value = c - '0';
    }
    else if (c >= 'a' && c <= 'f')
    {
      value = c - 'a' + 10;
    }
    else if (c >= 'A' && c <= 'F')
    {
      value = c - 'A' + 10;
    }
    else
    {
      return -1;
    }
    return value < radix ? value : -1;
  }

  // Accumulates digits into a base-256 byte array. The high byte is an extra sign byte:
  // 0x00 leaves a positive value's sign bit clear, and two's-complement negation turns
  // it into the 0xFF of a negative value.
  // Carry that would spill beyond typeSize is silently discarded (equivalent to
  // value mod 2^(8*typeSize)), and LiteralTooLong is logged once.
  // Returns null on an invalid digit. Only reached for >64-bit literals.
  private static Symbol? MakeWideInteger(SourceView view, ulong typeSize, uint radix, bool negative, Logger? logger)
  {
    var bytes = new byte[typeSize];

    uint len = 0;
    bool tooLarge = false;

    for (uint i = 0; i < view.Length; i++)
    {
      int digit = DigitValue(view[i], radix);
      if (digit < 0)
      {
        return null;
      }

      uint carry = (uint)digit;
      for (int b = 0; b < len; b++)
      {
        uint cur = bytes[b] * radix + carry;
        bytes[b] = (byte)cur;
        carry = cur >> 8;
      }
      while (carry != 0)
      {
        if (len < typeSize)
        {
          bytes[len++] = (byte)carry;
          carry >>= 8;
        }
        else
        {
          tooLarge = true;
          break;
        }
      }
    }

    if (negative)
    {
      int borrow = 1;
      for (int b = 0; b < bytes.Length; b++)
      {
        int t = (byte)~bytes[b] + borrow;
        bytes[b] = (byte)t;
        borrow = t >> 8;
      }
    }

    if (tooLarge)
    {
      logger?.Log(SymbolizerMessages.LiteralTooLong, view);
    }

    return MakeLiteralBytes(bytes, view.Line, view.Character);
  }

  public static Symbol? MakeString(SourceView view, ulong typeSize, Logger? logger)
  {
    var span = view.Span;
    int closingPos = 1;
    while (closingPos < span.Length && span[closingPos] != Grammar.StringQuote)
    {
      closingPos++;
    }

    SourceView content = view.Trim(1, view.Length - (uint)closingPos);
    SourceView suffix = view.Trim((uint)closingPos + 1);

    if (typeSize > content.Length)
    {
      logger?.Log(SymbolizerMessages.LiteralTooShort, content);
    }
    if (typeSize < content.Length)
    {
      logger?.Log(SymbolizerMessages.LiteralTooLong, content);
    }

    var encoding = Encoding.UTF8;
    if (suffix.Length > 0)
    {
      var resolvedEnc = Keywords.GetEncoding(suffix);
      if (resolvedEnc == null)
      {
        logger?.Log(SymbolizerMessages.InvalidStringEncoding, suffix);
      }
      else
      {
        encoding = resolvedEnc;
      }
    }

    if (typeSize <= sizeof(ulong))
    {
      Span<ulong> buffer = stackalloc ulong[1];
      encoding.GetBytes(content.Span, MemoryMarshal.Cast<ulong, byte>(buffer));
      return MakeLiteralBuffer(buffer[0], view.Line, view.Character);
    }
    else
    {
      var bytes = encoding.GetBytes(content.Source, (int)content.Start, (int)content.Length);
      return MakeLiteralBytes(bytes, view.Line, view.Character);
    }
  }

  private static Symbol MakeIdentifier(SourceView view)
  {
    var bytes = Encoding.UTF8.GetBytes(view.Source, (int)view.Start, (int)view.Length);
    return new()
    {
      Kind = SymbolKind.Identifier,
      Buffer = 0,
      Bytes = bytes,
      Line = view.Line,
      Char = view.Character,
    };
  }


  private static Symbol MakeLiteralBuffer(ulong buffer, uint line, uint character)
  {
    return new()
    {
      Kind = SymbolKind.Literal,
      Buffer = buffer,
      Bytes = null,
      Line = line,
      Char = character,
    };
  }

  private static Symbol MakeLiteralBytes(byte[] bytes, uint line, uint character)
  {
    return new()
    {
      Kind = SymbolKind.Literal,
      Buffer = 0,
      Bytes = bytes,
      Line = line,
      Char = character,
    };
  }
}
