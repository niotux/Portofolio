#pragma once
#include "ErrorMessages.h"
#include "IdList.h"
#include "Log.h"
#include "Parser.h"
#include "Tokenizer.h"

#include <stdint.h>

typedef struct
{
  LANIdList* Ids;
  LANAstModule* Module;
  size_t Cursor;

} Analyzer;

// Canonical pointer type, used to assert that a value is of pointer type. Its
// DataBuffer mirrors the LANTokenDataBuffer_TypePtr sentinel ((size_t)-1), which
// is what the tokenizer stores for ".ptr".
static LANToken PtrType = {
  .Kind = LANTokenKind_Temp_Ptr,
  .Text = NULL,
  .Line = 0,
  .Char = 0,
  .DataBuffer = (size_t)-1,
  .Components = NULL,
};

#define MustBe(message, token, comparer, fault, context) \
  if (comparer != token)                                 \
  {                                                      \
    LANLogContext(&message, context, fault);             \
    return;                                              \
  }

#define MustNotBe(message, token, comparer, fault, context) \
  if (comparer == token)                                    \
  {                                                         \
    LANLogContext(&message, context, fault);                \
    return;                                                 \
  }

#define ShouldBe(message, token, comparer, fault, context) \
  if (comparer != token)                                   \
  {                                                        \
    LANLogContext(&message, context, fault);               \
  }

#define ShouldBeOfKind(message, token, kind, fault, context) \
  if (token == NULL)                                         \
  {                                                          \
    LANLogContext(&message, context, fault);                 \
  }                                                          \
  else if (kind != token->Kind)                              \
  {                                                          \
    LANLogContext(&message, context, fault);                 \
  }

#define ShouldNotBe(message, token, comparer, fault, context) \
  if (comparer == token)                                      \
  {                                                           \
    LANLogContext(&message, context, fault);                  \
  }

#define ShouldMatch(token, type, context)                                            \
  {                                                                                  \
    LANIdListResponse response = LANIdList_CheckId(az->Ids, token, type);            \
    if (response.Status == NotMatch)                                                 \
    {                                                                                \
      LANLogContext(&LANMsg_Error_Analyzer_ParameterOfWrongSize, context, token);    \
    }                                                                                \
    else if (response.Status == Absent)                                              \
    {                                                                                \
      LANLogContext(&LANMsg_Error_Analyzer_ParameterMustBeExisting, context, token); \
    }                                                                                \
  }

#define ShouldMatchOrBeNew(token, type, context)                                  \
  {                                                                               \
    LANIdListResponse response = LANIdList_CheckId(az->Ids, token, type);         \
    if (response.Status == NotMatch)                                              \
    {                                                                             \
      LANLogContext(&LANMsg_Error_Analyzer_ParameterOfWrongSize, context, token); \
    }                                                                             \
    else if (response.Status == Absent)                                           \
    {                                                                             \
      LANIdList_AddId(az->Ids, token, type);                                      \
    }                                                                             \
  }

#define ShouldBeNew(token, type, context)                                       \
  {                                                                             \
    LANIdListResponse response = LANIdList_CheckId(az->Ids, token, type);       \
    if (response.Status == Absent)                                              \
    {                                                                           \
      LANIdList_AddId(az->Ids, token, type);                                    \
    }                                                                           \
    else                                                                        \
    {                                                                           \
      LANLogContext(&LANMsg_Error_Analyzer_ParameterMustBeNew, context, token); \
    }                                                                           \
  }

#define ShouldMatchReturn(value, context)                                               \
  {                                                                                     \
    LANIdListResponse response = LANIdList_CheckRetType(az->Ids, value);                \
    if (response.Status == NotMatch)                                                    \
    {                                                                                   \
      LANLogContext(&LANMsg_Error_Analyzer_RetTypeNotMatchingFunction, context, value); \
    }                                                                                   \
    else if (response.Status == Absent)                                                 \
    {                                                                                   \
      LANLogContext(&LANMsg_Error_Analyzer_RetTypeNotMatchingFunction, context, value); \
    }                                                                                   \
  }

#define ShouldBeLabel(token, context)                                     \
  if (!LANIdList_ResolveLabel(az->Ids, token))                            \
  {                                                                       \
    LANLogContext(&LANMsg_Error_Analyzer_UndefinedLabel, context, token); \
  }

// .Type Id
static void Analyze_LdArg(Analyzer* az, LANAstInstr* instr)
{
  MustNotBe(LANMsg_Error_Analyzer_RequiresTemp, instr->TypeArg0, NULL, NULL, instr->Op);
  ShouldBe(LANMsg_Error_Analyzer_UnexpectedTemp, instr->TypeArg1, NULL, instr->TypeArg1, instr->Op);

  MustNotBe(LANMsg_Error_Analyzer_RequiresValue, instr->ValueArg0, NULL, NULL, instr->Op);
  ShouldBeOfKind(LANMsg_Error_Analyzer_RequiresIdentifierValue, instr->ValueArg0, LANTokenKind_Val_Identifier, instr->ValueArg0, instr->Op);
  ShouldMatchOrBeNew(instr->ValueArg0, instr->TypeArg0, instr->Op);

  ShouldBe(LANMsg_Error_Analyzer_UnexpectedValue, instr->ValueArg1, NULL, instr->ValueArg1, instr->Op);
  ShouldBe(LANMsg_Error_Analyzer_UnexpectedValue, instr->ValueArg2, NULL, instr->ValueArg2, instr->Op);
  ShouldBe(LANMsg_Error_Analyzer_UnexpectedValue, instr->ValueArg3, NULL, instr->ValueArg3, instr->Op);
}

// .RetType Name
static void Analyze_Fun(Analyzer* az, LANAstInstr* instr)
{
  MustNotBe(LANMsg_Error_Analyzer_RequiresTemp, instr->TypeArg0, NULL, NULL, instr->Op);
  ShouldBe(LANMsg_Error_Analyzer_UnexpectedTemp, instr->TypeArg1, NULL, instr->TypeArg1, instr->Op);

  MustNotBe(LANMsg_Error_Analyzer_RequiresValue, instr->ValueArg0, NULL, NULL, instr->Op);
  ShouldBeOfKind(LANMsg_Error_Analyzer_RequiresIdentifierValue, instr->ValueArg0, LANTokenKind_Val_Identifier, instr->ValueArg0, instr->Op);

  ShouldBe(LANMsg_Error_Analyzer_UnexpectedValue, instr->ValueArg1, NULL, instr->ValueArg1, instr->Op);
  ShouldBe(LANMsg_Error_Analyzer_UnexpectedValue, instr->ValueArg2, NULL, instr->ValueArg2, instr->Op);
  ShouldBe(LANMsg_Error_Analyzer_UnexpectedValue, instr->ValueArg3, NULL, instr->ValueArg3, instr->Op);

  LANIdList_EnterFun(az->Ids, instr);
}

// Val   (no template; the type comes from the enclosing function)
static void Analyze_Ret(Analyzer* az, LANAstInstr* instr)
{
  ShouldBe(LANMsg_Error_Analyzer_UnexpectedTemp, instr->TypeArg0, NULL, instr->TypeArg0, instr->Op);
  ShouldBe(LANMsg_Error_Analyzer_UnexpectedTemp, instr->TypeArg1, NULL, instr->TypeArg1, instr->Op);

  MustNotBe(LANMsg_Error_Analyzer_RequiresValue, instr->ValueArg0, NULL, NULL, instr->Op);
  ShouldMatchReturn(instr->ValueArg0, LANIdList_GetCurrentFunName(az->Ids));

  ShouldBe(LANMsg_Error_Analyzer_UnexpectedValue, instr->ValueArg1, NULL, instr->ValueArg1, instr->Op);
  ShouldBe(LANMsg_Error_Analyzer_UnexpectedValue, instr->ValueArg2, NULL, instr->ValueArg2, instr->Op);
  ShouldBe(LANMsg_Error_Analyzer_UnexpectedValue, instr->ValueArg3, NULL, instr->ValueArg3, instr->Op);
}

// Name   (declaration; pre-registered for jumps by the function pre-scan)
static void Analyze_Label(Analyzer* az, LANAstInstr* instr)
{
  ShouldBe(LANMsg_Error_Analyzer_UnexpectedTemp, instr->TypeArg0, NULL, instr->TypeArg0, instr->Op);
  ShouldBe(LANMsg_Error_Analyzer_UnexpectedTemp, instr->TypeArg1, NULL, instr->TypeArg1, instr->Op);

  MustNotBe(LANMsg_Error_Analyzer_RequiresValue, instr->ValueArg0, NULL, NULL, instr->Op);
  ShouldBeOfKind(LANMsg_Error_Analyzer_RequiresIdentifierValue, instr->ValueArg0, LANTokenKind_Val_Identifier, instr->ValueArg0, instr->Op);

  ShouldBe(LANMsg_Error_Analyzer_UnexpectedValue, instr->ValueArg1, NULL, instr->ValueArg1, instr->Op);
  ShouldBe(LANMsg_Error_Analyzer_UnexpectedValue, instr->ValueArg2, NULL, instr->ValueArg2, instr->Op);
  ShouldBe(LANMsg_Error_Analyzer_UnexpectedValue, instr->ValueArg3, NULL, instr->ValueArg3, instr->Op);
}

// Label
static void Analyze_Jmp(Analyzer* az, LANAstInstr* instr)
{
  ShouldBe(LANMsg_Error_Analyzer_UnexpectedTemp, instr->TypeArg0, NULL, instr->TypeArg0, instr->Op);
  ShouldBe(LANMsg_Error_Analyzer_UnexpectedTemp, instr->TypeArg1, NULL, instr->TypeArg1, instr->Op);

  MustNotBe(LANMsg_Error_Analyzer_RequiresValue, instr->ValueArg0, NULL, NULL, instr->Op);
  ShouldBeOfKind(LANMsg_Error_Analyzer_RequiresIdentifierValue, instr->ValueArg0, LANTokenKind_Val_Identifier, instr->ValueArg0, instr->Op);
  ShouldBeLabel(instr->ValueArg0, instr->Op);

  ShouldBe(LANMsg_Error_Analyzer_UnexpectedValue, instr->ValueArg1, NULL, instr->ValueArg1, instr->Op);
  ShouldBe(LANMsg_Error_Analyzer_UnexpectedValue, instr->ValueArg2, NULL, instr->ValueArg2, instr->Op);
  ShouldBe(LANMsg_Error_Analyzer_UnexpectedValue, instr->ValueArg3, NULL, instr->ValueArg3, instr->Op);
}

// .Type Id, Val
static void Analyze_Set(Analyzer* az, LANAstInstr* instr)
{
  MustNotBe(LANMsg_Error_Analyzer_RequiresTemp, instr->TypeArg0, NULL, NULL, instr->Op);
  ShouldBe(LANMsg_Error_Analyzer_UnexpectedTemp, instr->TypeArg1, NULL, instr->TypeArg1, instr->Op);

  MustNotBe(LANMsg_Error_Analyzer_RequiresValue, instr->ValueArg0, NULL, NULL, instr->Op);
  ShouldBeOfKind(LANMsg_Error_Analyzer_RequiresIdentifierValue, instr->ValueArg0, LANTokenKind_Val_Identifier, instr->ValueArg0, instr->Op);
  ShouldMatchOrBeNew(instr->ValueArg0, instr->TypeArg0, instr->Op);

  ShouldNotBe(LANMsg_Error_Analyzer_RequiresValue, instr->ValueArg1, NULL, NULL, instr->Op);
  ShouldMatchOrBeNew(instr->ValueArg1, instr->TypeArg0, instr->Op);

  ShouldBe(LANMsg_Error_Analyzer_UnexpectedValue, instr->ValueArg2, NULL, instr->ValueArg2, instr->Op);
  ShouldBe(LANMsg_Error_Analyzer_UnexpectedValue, instr->ValueArg3, NULL, instr->ValueArg3, instr->Op);
}

// .Type Dest, Val1, Val2
static void Analyze_BinOperation(Analyzer* az, LANAstInstr* instr)
{
  MustNotBe(LANMsg_Error_Analyzer_RequiresTemp, instr->TypeArg0, NULL, NULL, instr->Op);
  ShouldBe(LANMsg_Error_Analyzer_UnexpectedTemp, instr->TypeArg1, NULL, instr->TypeArg1, instr->Op);

  MustNotBe(LANMsg_Error_Analyzer_RequiresValue, instr->ValueArg0, NULL, NULL, instr->Op);
  ShouldBeOfKind(LANMsg_Error_Analyzer_RequiresIdentifierValue, instr->ValueArg0, LANTokenKind_Val_Identifier, instr->ValueArg0, instr->Op);
  ShouldMatchOrBeNew(instr->ValueArg0, instr->TypeArg0, instr->Op);

  ShouldNotBe(LANMsg_Error_Analyzer_RequiresValue, instr->ValueArg1, NULL, NULL, instr->Op);
  ShouldMatch(instr->ValueArg1, instr->TypeArg0, instr->Op);

  ShouldNotBe(LANMsg_Error_Analyzer_RequiresValue, instr->ValueArg2, NULL, NULL, instr->Op);
  ShouldMatch(instr->ValueArg2, instr->TypeArg0, instr->Op);

  ShouldBe(LANMsg_Error_Analyzer_UnexpectedValue, instr->ValueArg3, NULL, instr->ValueArg3, instr->Op);
}

// .Type Dest, Val
static void Analyze_UnOperation(Analyzer* az, LANAstInstr* instr)
{
  MustNotBe(LANMsg_Error_Analyzer_RequiresTemp, instr->TypeArg0, NULL, NULL, instr->Op);
  ShouldBe(LANMsg_Error_Analyzer_UnexpectedTemp, instr->TypeArg1, NULL, instr->TypeArg1, instr->Op);

  MustNotBe(LANMsg_Error_Analyzer_RequiresValue, instr->ValueArg0, NULL, NULL, instr->Op);
  ShouldBeOfKind(LANMsg_Error_Analyzer_RequiresIdentifierValue, instr->ValueArg0, LANTokenKind_Val_Identifier, instr->ValueArg0, instr->Op);
  ShouldMatchOrBeNew(instr->ValueArg0, instr->TypeArg0, instr->Op);

  ShouldNotBe(LANMsg_Error_Analyzer_RequiresValue, instr->ValueArg1, NULL, NULL, instr->Op);
  ShouldMatch(instr->ValueArg1, instr->TypeArg0, instr->Op);

  ShouldBe(LANMsg_Error_Analyzer_UnexpectedValue, instr->ValueArg2, NULL, instr->ValueArg2, instr->Op);
  ShouldBe(LANMsg_Error_Analyzer_UnexpectedValue, instr->ValueArg3, NULL, instr->ValueArg3, instr->Op);
}

// .Type Dest, PtrSrc, Val
static void Analyze_Gep(Analyzer* az, LANAstInstr* instr)
{
  MustNotBe(LANMsg_Error_Analyzer_RequiresTemp, instr->TypeArg0, NULL, NULL, instr->Op);
  ShouldBe(LANMsg_Error_Analyzer_UnexpectedTemp, instr->TypeArg1, NULL, instr->TypeArg1, instr->Op);

  MustNotBe(LANMsg_Error_Analyzer_RequiresValue, instr->ValueArg0, NULL, NULL, instr->Op);
  ShouldBeOfKind(LANMsg_Error_Analyzer_RequiresIdentifierValue, instr->ValueArg0, LANTokenKind_Val_Identifier, instr->ValueArg0, instr->Op);
  ShouldMatchOrBeNew(instr->ValueArg0, instr->TypeArg0, instr->Op);

  ShouldNotBe(LANMsg_Error_Analyzer_UnexpectedValue, instr->ValueArg1, NULL, NULL, instr->Op);
  ShouldMatch(instr->ValueArg1, &PtrType, instr->Op);

  ShouldNotBe(LANMsg_Error_Analyzer_UnexpectedValue, instr->ValueArg2, NULL, NULL, instr->Op);
  ShouldMatch(instr->ValueArg2, instr->TypeArg0, instr->Op);

  ShouldBe(LANMsg_Error_Analyzer_UnexpectedValue, instr->ValueArg3, NULL, instr->ValueArg3, instr->Op);
}

// .Type PtrDest, Val
static void Analyze_AdressDestOperation(Analyzer* az, LANAstInstr* instr)
{
  MustNotBe(LANMsg_Error_Analyzer_RequiresTemp, instr->TypeArg0, NULL, NULL, instr->Op);
  ShouldBe(LANMsg_Error_Analyzer_UnexpectedTemp, instr->TypeArg1, NULL, instr->TypeArg1, instr->Op);

  MustNotBe(LANMsg_Error_Analyzer_RequiresValue, instr->ValueArg0, NULL, NULL, instr->Op);
  ShouldBeOfKind(LANMsg_Error_Analyzer_RequiresIdentifierValue, instr->ValueArg0, LANTokenKind_Val_Identifier, instr->ValueArg0, instr->Op);
  ShouldMatchOrBeNew(instr->ValueArg0, &PtrType, instr->Op);

  ShouldNotBe(LANMsg_Error_Analyzer_UnexpectedValue, instr->ValueArg1, NULL, NULL, instr->Op);
  ShouldMatch(instr->ValueArg1, instr->TypeArg0, instr->Op);

  ShouldBe(LANMsg_Error_Analyzer_UnexpectedValue, instr->ValueArg2, NULL, instr->ValueArg2, instr->Op);
  ShouldBe(LANMsg_Error_Analyzer_UnexpectedValue, instr->ValueArg3, NULL, instr->ValueArg3, instr->Op);
}

// .Type Dest, PtrSrc
static void Analyze_AdressSrcOperation(Analyzer* az, LANAstInstr* instr)
{
  MustNotBe(LANMsg_Error_Analyzer_RequiresTemp, instr->TypeArg0, NULL, NULL, instr->Op);
  ShouldBe(LANMsg_Error_Analyzer_UnexpectedTemp, instr->TypeArg1, NULL, instr->TypeArg1, instr->Op);

  MustNotBe(LANMsg_Error_Analyzer_RequiresValue, instr->ValueArg0, NULL, NULL, instr->Op);
  ShouldBeOfKind(LANMsg_Error_Analyzer_RequiresIdentifierValue, instr->ValueArg0, LANTokenKind_Val_Identifier, instr->ValueArg0, instr->Op);
  ShouldMatchOrBeNew(instr->ValueArg0, instr->TypeArg0, instr->Op);

  ShouldNotBe(LANMsg_Error_Analyzer_UnexpectedValue, instr->ValueArg1, NULL, NULL, instr->Op);
  ShouldMatch(instr->ValueArg1, &PtrType, instr->Op);

  ShouldBe(LANMsg_Error_Analyzer_UnexpectedValue, instr->ValueArg2, NULL, instr->ValueArg2, instr->Op);
  ShouldBe(LANMsg_Error_Analyzer_UnexpectedValue, instr->ValueArg3, NULL, instr->ValueArg3, instr->Op);
}

// .Type Label, Val1, Val2
static void Analyze_JumpCompare(Analyzer* az, LANAstInstr* instr)
{
  MustNotBe(LANMsg_Error_Analyzer_RequiresTemp, instr->TypeArg0, NULL, NULL, instr->Op);
  ShouldBe(LANMsg_Error_Analyzer_UnexpectedTemp, instr->TypeArg1, NULL, instr->TypeArg1, instr->Op);

  MustNotBe(LANMsg_Error_Analyzer_RequiresValue, instr->ValueArg0, NULL, NULL, instr->Op);
  ShouldBeOfKind(LANMsg_Error_Analyzer_RequiresIdentifierValue, instr->ValueArg0, LANTokenKind_Val_Identifier, instr->ValueArg0, instr->Op);
  ShouldBeLabel(instr->ValueArg0, instr->Op);

  ShouldNotBe(LANMsg_Error_Analyzer_UnexpectedValue, instr->ValueArg1, NULL, NULL, instr->Op);
  ShouldMatch(instr->ValueArg1, instr->TypeArg0, instr->Op);

  ShouldNotBe(LANMsg_Error_Analyzer_UnexpectedValue, instr->ValueArg2, NULL, NULL, instr->Op);
  ShouldMatch(instr->ValueArg2, instr->TypeArg0, instr->Op);

  ShouldBe(LANMsg_Error_Analyzer_UnexpectedValue, instr->ValueArg3, NULL, instr->ValueArg3, instr->Op);
}

// .TypeFrom .TypeTo Dest, Val
static void Analyze_Conversion(Analyzer* az, LANAstInstr* instr)
{
  MustNotBe(LANMsg_Error_Analyzer_RequiresTemp, instr->TypeArg0, NULL, NULL, instr->Op);
  MustNotBe(LANMsg_Error_Analyzer_UnexpectedTemp, instr->TypeArg1, NULL, NULL, instr->Op);

  MustNotBe(LANMsg_Error_Analyzer_RequiresValue, instr->ValueArg0, NULL, NULL, instr->Op);
  ShouldBeOfKind(LANMsg_Error_Analyzer_RequiresIdentifierValue, instr->ValueArg0, LANTokenKind_Val_Identifier, instr->ValueArg0, instr->Op);
  ShouldMatchOrBeNew(instr->ValueArg0, instr->TypeArg0, instr->Op);

  ShouldNotBe(LANMsg_Error_Analyzer_UnexpectedValue, instr->ValueArg1, NULL, NULL, instr->Op);
  ShouldMatch(instr->ValueArg1, instr->TypeArg1, instr->Op);

  ShouldBe(LANMsg_Error_Analyzer_UnexpectedValue, instr->ValueArg2, NULL, instr->ValueArg2, instr->Op);
  ShouldBe(LANMsg_Error_Analyzer_UnexpectedValue, instr->ValueArg3, NULL, instr->ValueArg3, instr->Op);
}

// FuncName Dest   (arguments follow as `arg` instructions)
static void Analyze_Call(Analyzer* az, LANAstInstr* instr)
{
  ShouldBe(LANMsg_Error_Analyzer_UnexpectedTemp, instr->TypeArg0, NULL, instr->TypeArg0, instr->Op);
  ShouldBe(LANMsg_Error_Analyzer_UnexpectedTemp, instr->TypeArg1, NULL, instr->TypeArg1, instr->Op);

  MustNotBe(LANMsg_Error_Analyzer_RequiresValue, instr->ValueArg0, NULL, NULL, instr->Op);
  ShouldBeOfKind(LANMsg_Error_Analyzer_RequiresIdentifierValue, instr->ValueArg0, LANTokenKind_Val_Identifier, instr->ValueArg0, instr->Op);

  MustNotBe(LANMsg_Error_Analyzer_RequiresValue, instr->ValueArg1, NULL, NULL, instr->Op);
  ShouldBeOfKind(LANMsg_Error_Analyzer_RequiresIdentifierValue, instr->ValueArg1, LANTokenKind_Val_Identifier, instr->ValueArg1, instr->Op);

  ShouldBe(LANMsg_Error_Analyzer_UnexpectedValue, instr->ValueArg2, NULL, instr->ValueArg2, instr->Op);
  ShouldBe(LANMsg_Error_Analyzer_UnexpectedValue, instr->ValueArg3, NULL, instr->ValueArg3, instr->Op);

  LANIdListResponse fun = LANIdList_CheckFun(az->Ids, instr->ValueArg0);
  if (fun.Status == Absent)
  {
    LANLogContext(&LANMsg_Error_Analyzer_UndefinedFunction, instr->Op, instr->ValueArg0);
    return;
  }

  ShouldMatchOrBeNew(instr->ValueArg1, fun.Type, instr->Op);

  size_t count = LANAstModule_InstrCount(az->Module);
  size_t index = 0;
  int tooMany = 0;
  while (az->Cursor < count)
  {
    LANAstInstr* argInstr = LANAstModule_InstrAt(az->Module, az->Cursor);
    if (argInstr->Op->Kind != LANTokenKind_Op_Arg)
    {
      break;
    }
    az->Cursor++;
    argInstr->Op->DataBuffer = index;
    LANIdListResponse param = LANIdList_CheckArg(az->Ids, index);
    if (param.Status == Absent)
    {
      tooMany = 1;
    }
    else
    {
      ShouldMatch(argInstr->ValueArg0, param.Type, instr->Op);
    }
    index++;
  }

  if (tooMany)
  {
    LANLogContext(&LANMsg_Error_Analyzer_UnexpectedValue, instr->Op, instr->Op);
  }
  else if (LANIdList_CheckArg(az->Ids, index).Status != Absent)
  {
    LANLogContext(&LANMsg_Error_Analyzer_RequiresValue, instr->Op, instr->Op);
  }
}

// Value   (only valid right after a call, where it is consumed)
static void Analyze_Arg(Analyzer* az, LANAstInstr* instr)
{
  LANLogContext(&LANMsg_Error_Analyzer_StrayArg, instr->Op, NULL);
}
