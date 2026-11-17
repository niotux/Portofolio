#pragma once

#include "lan.h"

#include <stdbool.h>
#include <stdint.h>

typedef struct LAN2LLVMContext LAN2LLVMContext;

typedef enum
{
  LAN2LLVMInt,
  LAN2LLVMFloat,
  LAN2LLVMLabel,
  LAN2LLVMPtr,
  LAN2LLVMFun,

} LAN2LLVMType;

typedef struct
{
  const LANString* Text;
  size_t FloatVariants;
  size_t IntVariants;
  LAN2LLVMType LastType;

} LAN2LLVMIdentifier;

LAN2LLVMContext* LAN2LLVMMakeContext(LANString* output);
void LAN2LLVMFreeContext(LAN2LLVMContext** inst);

void LAN2LLVMContext_SetOutput(LAN2LLVMContext* inst, LANString* output);
void LAN2LLVMContext_ResetCursor(LAN2LLVMContext* inst);
void LAN2LLVMContext_MoveCursor(LAN2LLVMContext* inst, size_t by);
LANString* LAN2LLVMContext_Output(LAN2LLVMContext* ctx);
bool LAN2LLVMContext_HasOutput(LAN2LLVMContext* ctx);
size_t LAN2LLVMContext_Cursor(LAN2LLVMContext* ctx);
void LAN2LLVMContext_GrowIdList(LAN2LLVMContext* ctx);
void LAN2LLVMContext_ClearIdList(LAN2LLVMContext* ctx);
LAN2LLVMIdentifier* LAN2LLVMContext_RegisterId(LAN2LLVMContext* ctx, LANToken* id, LAN2LLVMType type);
LAN2LLVMIdentifier* LAN2LLVMContext_GetId(LAN2LLVMContext* ctx, LANToken* id);
LANAstInstr* LAN2LLVMContext_GetOuterFunc(LAN2LLVMContext* inst);
void LAN2LLVMContext_SetOuterFunc(LAN2LLVMContext* inst, LANAstInstr* instr);
LANAstInstr* LAN2LLVMContext_GetLastCalledFunc(LAN2LLVMContext* inst);
void LAN2LLVMContext_SetLastCalledFunc(LAN2LLVMContext* inst, LANAstInstr* instr);