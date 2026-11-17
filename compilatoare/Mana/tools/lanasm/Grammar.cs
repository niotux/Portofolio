using System.Reflection.Metadata;

namespace LAN.Assembler;

/// <summary>
/// The single source of truth for LAN's lexical grammar. Every terminal the
/// tokenizer recognises lives here — change it in one place, not scattered
/// across the scanner. Want '#' comments instead of '//'? Edit <see cref="Comment"/>.
/// </summary>
public static class Grammar
{
  // Trivia
  public const string Comment = "//";
  public const char Separator = ',';

  // Literals
  public const char StringQuote = '"';
  public const char NegativeSign = '-';

  // Identifier alphabet
  public const char Underscore = '_';
  public const char Sigil = '$';
  public const char IdentifierJoin = '.';

  /// <summary>
  /// Standalone symbols, ordered longest-first so the scanner longest-matches
  /// ('::' before ':' would-be). Each is its own token; none joins a blob.
  /// </summary>
  public const string TemplateStart = ".";
  public const string InstructionEnd = ";";
  public const string Namespace = "::";
  public const string StructureCompositor = "+";
  public const string Star = "*";
  public const string GenericBegin = "[";
  public const string GenericEnd = "]";
  public static readonly string[] Symbols =
  [
    TemplateStart,
    InstructionEnd,
    Namespace,
    StructureCompositor,
    Star,
    GenericBegin,
    GenericEnd,
  ];

  // Number literals
  public const string HexMarker = "0x";
  public const string BinaryMarker = "0b";
  public const string OctalMarker = "0o";
  public const string DecimalMarker = "";

  public const uint HexRadix = 16;
  public const uint BinaryRadix = 2;
  public const uint OctalRadix = 8;
  public const uint DecimalRadix = 10;

  public const char DecimalPoint = '.';   // same char as IdentifierJoin
  public const char Exponent = 'e';       // matched case-insensitively (e / E)

  // String literal encoding suffixes (absent => Utf8)
  public const string EncodingAscii = "ascii";
  public const string EncodingUtf8 = "utf8";
  public const string EncodingUtf16 = "utf16";
  public const string EncodingUtf32 = "utf32";
  public const string EncodingLatin = "latin";

  public static bool IsBoundary(char c) => char.IsWhiteSpace(c) || c == Separator;

  public static bool IsIdentifierStart(char c)
    => char.IsAsciiLetter(c) || c == Underscore || c == Sigil;

  public static bool IsIdentifierContinue(char c)
    => char.IsAsciiLetterOrDigit(c) || c == Underscore || c == Sigil || c == IdentifierJoin;

  public static bool IsNumberStart(char c) => char.IsAsciiDigit(c);

  public static bool IsNumberContinue(char c) => char.IsAsciiLetterOrDigit(c) || c == '.';
}
