#pragma once
#include "String.h"

#include <stdint.h>
#include <stdlib.h>

extern const size_t LANTokenDataBuffer_TypePtr;

typedef struct LANTokenArray LANTokenArray;

typedef enum
{
  LANTokenKind_Unknown,

  LANTokenKind_Op,

  LANTokenKind_Op_Fun,
  LANTokenKind_Op_LdArg,
  LANTokenKind_Op_Ret,

  LANTokenKind_Op_Call,
  LANTokenKind_Op_Arg,

  LANTokenKind_Op_Set,

  LANTokenKind_Op_Add,
  LANTokenKind_Op_Sub,
  LANTokenKind_Op_Mul,
  LANTokenKind_Op_Div,
  LANTokenKind_Op_Mod,

  LANTokenKind_Op_IDiv,
  LANTokenKind_Op_IMod,

  LANTokenKind_Op_FAdd,
  LANTokenKind_Op_FSub,
  LANTokenKind_Op_FMul,
  LANTokenKind_Op_FDiv,
  LANTokenKind_Op_FMod,

  LANTokenKind_Op_Or,
  LANTokenKind_Op_And,
  LANTokenKind_Op_Xor,
  LANTokenKind_Op_ShL,
  LANTokenKind_Op_ShR,
  LANTokenKind_Op_IShR,
  LANTokenKind_Op_Not,

  LANTokenKind_Op_Alloc,
  LANTokenKind_Op_Write,
  LANTokenKind_Op_Read,
  LANTokenKind_Op_Gep,

  LANTokenKind_Op_Conv,
  LANTokenKind_Op_IConv,
  LANTokenKind_Op_FConv,
  LANTokenKind_Op_IToF,
  LANTokenKind_Op_UToF,
  LANTokenKind_Op_FToI,
  LANTokenKind_Op_FToU,

  LANTokenKind_Op_Label,
  LANTokenKind_Op_Jmp,
  LANTokenKind_Op_Jeq,
  LANTokenKind_Op_Jne,
  LANTokenKind_Op_Jlt,
  LANTokenKind_Op_Jgt,
  LANTokenKind_Op_Jle,
  LANTokenKind_Op_Jge,
  LANTokenKind_Op_IJlt,
  LANTokenKind_Op_IJgt,
  LANTokenKind_Op_IJle,
  LANTokenKind_Op_IJge,
  LANTokenKind_Op_FJlt,
  LANTokenKind_Op_FJgt,
  LANTokenKind_Op_FJle,
  LANTokenKind_Op_FJge,

  LANTokenKind_Val,

  LANTokenKind_Val_Identifier,
  LANTokenKind_Val_FloatLiteral,
  LANTokenKind_Val_PosIntLiteral,
  LANTokenKind_Val_NegIntLiteral,
  LANTokenKind_Val_HexLiteral,
  LANTokenKind_Val_BinLiteral,
  LANTokenKind_Val_OctLiteral,
  LANTokenKind_Val_StringLiteral,
  LANTokenKind_Val_BoolLiteral,

  LANTokenKind_Temp,

  LANTokenKind_Temp_Simple,
  LANTokenKind_Temp_Composed,
  LANTokenKind_Temp_Ptr,

  LANTokenKind_Symbol,

  LANTokenKind_Symbol_InstrEnd,
  LANTokenKind_Symbol_TempSeparator,
  LANTokenKind_Symbol_BlockBegin,
  LANTokenKind_Symbol_BlockEnd,

} LANTokenKind;

int LANTokenKind_IsOp(LANTokenKind kind);
int LANTokenKind_IsVal(LANTokenKind kind);
int LANTokenKind_IsTemp(LANTokenKind kind);
int LANTokenKind_IsSymbol(LANTokenKind kind);
uint8_t LANTokenKind_ToInstruction(LANTokenKind kind);

typedef struct
{
  const LANTokenKind Kind;
  const LANString* Text;
  const size_t Line; // 1-based
  const size_t Char; // 1-based column of the first character
  size_t DataBuffer;
  LANTokenArray* Components; // NULL except for LANTokenKind_Temp_Composed

} LANToken;

typedef struct
{
  const LANToken* Start;
  const size_t Count;

} LANTokenSlice;

LANStringArray* LANSplitTokens(LANString* source);
LANTokenArray* LANMakeTokens(LANStringArray* tokens);
LANTokenArray* LANMakeTokenArray(size_t count);

LANTokenArray* LANTokenizeSource(LANString* source);

// Prepend a synthetic `fun .4 main ;` so top-level code becomes the body of
// an ordinary function and the analyzer needs no special case for it.
void LANInjectMain(LANTokenArray** instRef);

void LANFreeTokens(LANTokenArray** instRef);

// Structural equality of two type tokens: compares DataBuffer recursively
// through the component tree. Two NULL tokens are considered equal.
int LANToken_EqualsData(LANToken* a, LANToken* b);

size_t LANTokenArray_Count(LANTokenArray* inst);
LANToken* LANTokenArray_At(LANTokenArray* inst, size_t index);
LANTokenSlice LANTokenArray_Slice(LANTokenArray* inst, size_t start, size_t length);

const char* LANTokenKind_Name(LANTokenKind kind);