using LAN.Logging;

namespace LAN.Assembler.Pipeline2;

/// <summary>
/// Tokenizer message catalog — every tokenizer diagnostic as a named, reusable ErrorMessage.
/// The Tokenizer stage uses only InvalidCharacter and UnterminatedString; the rest belong to the Symbolizer.
/// </summary>
public static class TokenizerMessages
{
  public static readonly ErrorMessage UnterminatedString = new(LogLevel.Error, "Unterminated string literal");
  public static readonly ErrorMessage InvalidCharacter = new(LogLevel.Error, "Invalid character");
}

