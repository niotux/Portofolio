#pragma once

#include "Core.h"
#include "Parser.h"

#include <stdint.h>
#include <stdlib.h>

typedef struct LANBytecode LANBytecode;

LANBytecode* LANMakeBytecodeFromAst(LANAstModule* module, LANVersion version, LANArch arch);
LANBytecode* LANMakeBytecodeFromBytes(const void* bytes, size_t count);
void LANFreeBytecode(LANBytecode** instRef);

LANVersion LANBytecode_GetVersion(const LANBytecode* inst);
uint32_t LANBytecode_GetMagicNumber(const LANBytecode* inst);
uint64_t LANBytecode_GetConstantPoolCount(const LANBytecode* inst);
uint64_t LANBytecode_GetInstrCount(const LANBytecode* inst);
inline void* LANBytecode_Bytes(const LANBytecode* inst);
inline size_t LANBytecode_Count(const LANBytecode* inst);

typedef struct LANBytecodeStream LANBytecodeStream;

typedef struct
{
  void* Data;

} LANBytecodeConstString;

typedef struct
{
  uint8_t Size;
  void* Data;

} LANBytecodeConst8;

typedef struct
{
  uint16_t Size;
  void* Data;

} LANBytecodeConst16;

typedef struct
{
  uint32_t Size;
  void* Data;

} LANBytecodeConst32;

typedef struct
{
  uint64_t Size;
  void* Data;

} LANBytecodeConst64;

typedef union
{
  LANBytecodeConstString String;
  LANBytecodeConst8 Sized8;
  LANBytecodeConst16 Sized16;
  LANBytecodeConst32 Sized32;
  LANBytecodeConst64 Sized64;

} LANBytecodeConst;

typedef struct
{
  uint8_t EncodingTag;
  LANBytecodeConst Constant;

} LANBytecodeSymbol;

void LANBytecodeStream_MoveNextInstr(LANBytecodeStream* inst);
uint8_t LANBytecodeStream_GetOp(LANBytecodeStream* inst);
LANBytecodeSymbol LANBytecodeStream_GetTemplate0(LANBytecodeStream* inst);
LANBytecodeSymbol LANBytecodeStream_GetTemplate1(LANBytecodeStream* inst);
LANBytecodeSymbol LANBytecodeStream_GetValue0(LANBytecodeStream* inst);
LANBytecodeSymbol LANBytecodeStream_GetValue1(LANBytecodeStream* inst);
LANBytecodeSymbol LANBytecodeStream_GetValue2(LANBytecodeStream* inst);
LANBytecodeSymbol LANBytecodeStream_GetValue3(LANBytecodeStream* inst);
