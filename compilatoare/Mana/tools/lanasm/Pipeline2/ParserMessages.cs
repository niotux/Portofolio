using LAN.Logging;

namespace LAN.Assembler.Pipeline2;

public static class ParserMessages
{
  public static readonly ErrorMessage UnknownOpCode = new(LogLevel.Error, "Unknown opcode");
  public static readonly ErrorMessage UnexpectedSymbol = new(LogLevel.Error, "Unexpected symbol. Expected a template marker or a value");
  public static readonly ErrorMessage TooManyTemplates = new(LogLevel.Error, "Too many templates for operation");
  public static readonly ErrorMessage TooManyValues = new(LogLevel.Error, "Too many values for operation");
  public static readonly ErrorMessage TooFewTemplates = new(LogLevel.Error, "Too few templates for operation");
  public static readonly ErrorMessage TooFewValues = new(LogLevel.Error, "Too few values for operation");
  public static readonly ErrorMessage IncompleteTemplate = new(LogLevel.Error, "Incomplete template");
  public static readonly ErrorMessage IncompleteValue = new(LogLevel.Error, "Incomplete value");
  public static readonly ErrorMessage MissingTypeSize = new(LogLevel.Error, "Incomplete value");
  public static readonly ErrorMessage MissingValue = new(LogLevel.Error, "Incomplete value");
  public static readonly ErrorMessage ExpectedInstruction = new(LogLevel.Error, "Instructions must start with a valid op code");
}