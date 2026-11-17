#pragma once
#include "ErrorMessages.h"
#include "Log.h"

#include <stdio.h>

#define CHECK_OP                                                  \
  if (LANTokenKind_IsOp(token->Kind))                             \
  {                                                               \
    LANLog(&LANMsg_Error_Parser_ExpectedEndInstrBeforeOp, token); \
    dest[0].Op = token;                                           \
    return ParsedOp;                                              \
  }

typedef enum
{
  ParsedInstrEnd,
  ParsedOp,
  ParsedTemp0,
  ParsedTemp1,
  ParsedVal0,
  ParsedVal1,
  ParsedVal2,
  ParsedVal3,
  InstrFatal,

} ParserStage;

typedef ParserStage (*ParsingStageFunc)(LANToken* token, LANAstInstr* dest);

static ParserStage ParseFrom_InstrEnd(LANToken* token, LANAstInstr* dest)
{
  if (!LANTokenKind_IsOp(token->Kind))
  {
    LANLog(&LANMsg_Error_Parser_ExpectedOp, token);
    return InstrFatal;
  }

  dest->Op = token;
  return ParsedOp;
}

static ParserStage ParseFrom_Op(LANToken* token, LANAstInstr* dest)
{
  CHECK_OP

  if (token->Kind == LANTokenKind_Symbol_InstrEnd)
  {
    return ParsedInstrEnd;
  }
  if (LANTokenKind_IsTemp(token->Kind))
  {
    dest->TypeArg0 = token;
    return ParsedTemp0;
  }
  if (LANTokenKind_IsVal(token->Kind))
  {
    dest->ValueArg0 = token;
    return ParsedVal0;
  }
  LANLog(&LANMsg_Error_Parser_UnexpectedToken, token);
  return ParsedInstrEnd;
}

static ParserStage ParseFrom_Temp0(LANToken* token, LANAstInstr* dest)
{
  CHECK_OP

  if (token->Kind == LANTokenKind_Symbol_InstrEnd)
  {
    return ParsedInstrEnd;
  }
  if (LANTokenKind_IsTemp(token->Kind))
  {
    dest->TypeArg1 = token;
    return ParsedTemp1;
  }
  if (LANTokenKind_IsVal(token->Kind))
  {
    dest->ValueArg0 = token;
    return ParsedVal0;
  }

  LANLog(&LANMsg_Error_Parser_UnexpectedToken, token);
  return InstrFatal;
}

static ParserStage ParseFrom_Temp1(LANToken* token, LANAstInstr* dest)
{
  CHECK_OP

  if (token->Kind == LANTokenKind_Symbol_InstrEnd)
  {
    return ParsedInstrEnd;
  }
  if (LANTokenKind_IsVal(token->Kind))
  {
    dest->ValueArg0 = token;
    return ParsedVal0;
  }

  LANLog(&LANMsg_Error_Parser_UnexpectedToken, token);
  return ParsedVal0;
}

static ParserStage ParseFrom_Val0(LANToken* token, LANAstInstr* dest)
{
  CHECK_OP

  if (token->Kind == LANTokenKind_Symbol_InstrEnd)
  {
    return ParsedInstrEnd;
  }
  if (LANTokenKind_IsVal(token->Kind))
  {
    dest->ValueArg1 = token;
    return ParsedVal1;
  }

  LANLog(&LANMsg_Error_Parser_UnexpectedToken, token);
  return ParsedVal1;
}

static ParserStage ParseFrom_Val1(LANToken* token, LANAstInstr* dest)
{
  CHECK_OP

  if (token->Kind == LANTokenKind_Symbol_InstrEnd)
  {
    return ParsedInstrEnd;
  }
  if (LANTokenKind_IsVal(token->Kind))
  {
    dest->ValueArg2 = token;
    return ParsedVal2;
  }

  LANLog(&LANMsg_Error_Parser_UnexpectedToken, token);
  return ParsedVal2;
}

static ParserStage ParseFrom_Val2(LANToken* token, LANAstInstr* dest)
{
  CHECK_OP

  if (token->Kind == LANTokenKind_Symbol_InstrEnd)
  {
    return ParsedInstrEnd;
  }
  if (LANTokenKind_IsVal(token->Kind))
  {
    dest->ValueArg3 = token;
    return ParsedVal3;
  }

  LANLog(&LANMsg_Error_Parser_UnexpectedToken, token);
  return ParsedVal3;
}

static ParserStage ParseFrom_Val3(LANToken* token, LANAstInstr* dest)
{
  if (token->Kind == LANTokenKind_Symbol_InstrEnd)
  {
    return ParsedInstrEnd;
  }

  CHECK_OP

  LANLog(&LANMsg_Error_Parser_ExpectedEndInstr, token);
  return InstrFatal;
}

static const ParsingStageFunc ParserStagesDelegation[] = {
  [ParsedInstrEnd] = ParseFrom_InstrEnd,
  [ParsedOp] = ParseFrom_Op,
  [ParsedTemp0] = ParseFrom_Temp0,
  [ParsedTemp1] = ParseFrom_Temp1,
  [ParsedVal0] = ParseFrom_Val0,
  [ParsedVal1] = ParseFrom_Val1,
  [ParsedVal2] = ParseFrom_Val2,
  [ParsedVal3] = ParseFrom_Val3,
};
