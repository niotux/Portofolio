using LAN.Logging;

namespace LAN.Assembler.Pipeline2;

public static class SymbolizerMessages
{
  public static readonly ErrorMessage InvalidIntegerLiteral = new(LogLevel.Error, "Invalid integer literal");
  public static readonly ErrorMessage InvalidFloatLiteral = new(LogLevel.Error, "Invalid float literal");
  public static readonly ErrorMessage LiteralTooLong = new(LogLevel.Warning, "Literal too long. The value will be choped");
  public static readonly ErrorMessage LiteralTooShort = new(LogLevel.Warning, "Literal too short. Padding with 0");
  public static readonly ErrorMessage InvalidTypeSize = new(LogLevel.Error, "Invalid typesize");
  public static readonly ErrorMessage InvalidStringEncoding = new(LogLevel.Error, "Invalid string encoding");
  public static readonly ErrorMessage ExpectedDecimalIntegerAfterMinus = new(LogLevel.Error, "Expected a decimal integer after the minus");
}
