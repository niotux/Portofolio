#include "ErrorMessages.h"
#include "Log.h"

const LANLogMessage LANMsg_Fatal_Program_OutOfBounds = {
  .Level = LANLogLevel_Fatal,
  .Message = "Something went out of bounds... compiler baaad!!",
};

const LANLogMessage LANMsg_Error_Parser_ExpectedOp = {
  .Level = LANLogLevel_Error,
  .Message = "Expected an op code but got",
};
const LANLogMessage LANMsg_Error_Parser_ExpectedEndInstrBeforeOp = {
  .Level = LANLogLevel_Warning,
  .Message = "Expected a ';' before next op code",
};
const LANLogMessage LANMsg_Error_Parser_ExpectedEndInstr = {
  .Level = LANLogLevel_Warning,
  .Message = "Expected a ';'. Invalid token",
};
const LANLogMessage LANMsg_Error_Parser_UnexpectedToken = {
  .Level = LANLogLevel_Error,
  .Message = "Unexpected identifier",
};
const LANLogMessage LANMsg_Error_Parser_ExpectedTemp = {
  .Level = LANLogLevel_Error,
  .Message = "Expected a type size template for the op but got",
};

const LANLogMessage LANMsg_Error_Analyzer_RequiresTemp = {
  .Level = LANLogLevel_Error,
  .Message = "Too few template parameters for instruction",
};
const LANLogMessage LANMsg_Error_Analyzer_UnexpectedTemp = {
  .Level = LANLogLevel_Error,
  .Message = "Too many template parameters for instruction",
};
const LANLogMessage LANMsg_Error_Analyzer_RequiresIdentifierValue = {
  .Level = LANLogLevel_Error,
  .Message = "An identifier value parameter was expected for instruction",
};
const LANLogMessage LANMsg_Error_Analyzer_RequiresValue = {
  .Level = LANLogLevel_Error,
  .Message = "Too few value parameters for instruction",
};
const LANLogMessage LANMsg_Error_Analyzer_UnexpectedValue = {
  .Level = LANLogLevel_Error,
  .Message = "Too many value parameters for instruction",
};
const LANLogMessage LANMsg_Error_Analyzer_ParameterOfWrongSize = {
  .Level = LANLogLevel_Error,
  .Message = "Parameter has the wrong type size for instruction",
};
const LANLogMessage LANMsg_Error_Analyzer_ParameterMustBeExisting = {
  .Level = LANLogLevel_Error,
  .Message = "Parameter must be an already existing variable",
};
const LANLogMessage LANMsg_Error_Analyzer_ParameterMustBeNew = {
  .Level = LANLogLevel_Error,
  .Message = "Parameter must be a new variable",
};
const LANLogMessage LANMsg_Error_Analyzer_UndefinedLabel = {
  .Level = LANLogLevel_Error,
  .Message = "Jump target is not a defined label",
};
const LANLogMessage LANMsg_Error_Analyzer_UndefinedFunction = {
  .Level = LANLogLevel_Error,
  .Message = "Call to a function that does not exist",
};
const LANLogMessage LANMsg_Error_Analyzer_RetTypeNotMatchingFunction = {
  .Level = LANLogLevel_Error,
  .Message = "Return parameter does not match the return type of function",
};
const LANLogMessage LANMsg_Error_Analyzer_StrayArg = {
  .Level = LANLogLevel_Error,
  .Message = "A preceding call instruction is required before instruction",
};

const LANLogMessage LANMsg_Error_Tokenizer_UnterminatedString = {
  .Level = LANLogLevel_Error,
  .Message = "Unterminated string literal",
};
const LANLogMessage LANMsg_Error_Tokenizer_InvalidCharacter = {
  .Level = LANLogLevel_Error,
  .Message = "Unknown character in source",
};
const LANLogMessage LANMsg_Error_Tokenizer_InvalidNumericLiteral = {
  .Level = LANLogLevel_Error,
  .Message = "Unknown character in source",
};
const LANLogMessage LANMsg_Error_Tokenizer_EmptyHexLiteral = {
  .Level = LANLogLevel_Error,
  .Message = "Hex literal has no digits after '0x'",
};
const LANLogMessage LANMsg_Error_Tokenizer_InvalidHexLiteral = {
  .Level = LANLogLevel_Error,
  .Message = "Invalid character in hex literal",
};
const LANLogMessage LANMsg_Error_Tokenizer_EmptyBinLiteral = {
  .Level = LANLogLevel_Error,
  .Message = "Binary literal has no digits after '0b'",
};
const LANLogMessage LANMsg_Error_Tokenizer_InvalidBinLiteral = {
  .Level = LANLogLevel_Error,
  .Message = "Invalid character in binary literal, expected '0' or '1'",
};
const LANLogMessage LANMsg_Error_Tokenizer_EmptyOctLiteral = {
  .Level = LANLogLevel_Error,
  .Message = "Octal literal has no digits after '0o'",
};
const LANLogMessage LANMsg_Error_Tokenizer_InvalidOctLiteral = {
  .Level = LANLogLevel_Error,
  .Message = "Invalid character in octal literal, expected '0' to '7'",
};
const LANLogMessage LANMsg_Error_Tokenizer_InvalidFloatLiteral = {
  .Level = LANLogLevel_Error,
  .Message = "Invalid float literal",
};
const LANLogMessage LANMsg_Error_Tokenizer_InvalidPosIntLiteral = {
  .Level = LANLogLevel_Error,
  .Message = "Invalid integer literal",
};
const LANLogMessage LANMsg_Error_Tokenizer_InvalidNegIntLiteral = {
  .Level = LANLogLevel_Error,
  .Message = "Invalid negative integer literal",
};
const LANLogMessage LANMsg_Error_Tokenizer_LiteralTooLong = {
  .Level = LANLogLevel_Error,
  .Message = "Numeric literal is too long",
};
const LANLogMessage LANMsg_Error_Tokenizer_StrayMinus = {
  .Level = LANLogLevel_Error,
  .Message = "Stray '-' not followed by a digit",
};
const LANLogMessage LANMsg_Error_Tokenizer_InvalidTemplate = {
  .Level = LANLogLevel_Error,
  .Message = "Invalid template literal",
};
const LANLogMessage LANMsg_Warning_Tokenizer_PtrSentinelCollision = {
  .Level = LANLogLevel_Warning,
  .Message = "Numeric type size equals the pointer sentinel value; this component will be treated as a pointer",
};