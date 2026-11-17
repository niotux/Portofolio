#include "Context.h"

#include <stdarg.h>
#include <stdbool.h>
#include <stdio.h>
#include <stdlib.h>

static const size_t IdListResizeFactor = 2;

struct LAN2LLVMContext
{
  size_t Cursor;
  LANString* Output;
  LANAstInstr* OuterFunc;
  LANAstInstr* LastCall;
  LAN2LLVMIdentifier* IdList;
  size_t IdCapacity;
  size_t IdCount;
};

LAN2LLVMContext* LAN2LLVMMakeContext(LANString* output)
{
  LAN2LLVMContext* inst = malloc(sizeof(LAN2LLVMContext));
  inst->Cursor = 0;
  inst->IdCapacity = 4;
  inst->IdCount = 0;
  inst->IdList = NULL;
  inst->OuterFunc = NULL;
  inst->Output = output;

  return inst;
}

void LAN2LLVMFreeContext(LAN2LLVMContext** instRef)
{
  if (instRef == NULL)
  {
    return;
  }
  if (*instRef == NULL)
  {
    return;
  }
  LAN2LLVMContext_ClearIdList(*instRef);
  free(*instRef);
  *instRef = NULL;
}

void LAN2LLVMContext_ResetCursor(LAN2LLVMContext* inst)
{
  inst->Cursor = 0;
}

void LAN2LLVMContext_SetOutput(LAN2LLVMContext* inst, LANString* output)
{
  inst->Output = output;
}

void LAN2LLVMContext_MoveCursor(LAN2LLVMContext* inst, size_t by)
{
  if (inst->Output != NULL)
  {
    if (inst->Cursor + by >= LANString_Count(inst->Output))
    {
      return;
    }
  }
  inst->Cursor += by;
}

LANString* LAN2LLVMContext_Output(LAN2LLVMContext* ctx)
{
  return ctx->Output;
}

bool LAN2LLVMContext_HasOutput(LAN2LLVMContext* ctx)
{
  return ctx->Output != NULL;
}

size_t LAN2LLVMContext_Cursor(LAN2LLVMContext* ctx)
{
  return ctx->Cursor;
}

void LAN2LLVMContext_GrowIdList(LAN2LLVMContext* ctx)
{
  ctx->IdCount++;
  if (ctx->IdCount > ctx->IdCapacity)
  {
    ctx->IdCapacity *= IdListResizeFactor;
    ctx->IdList = (LAN2LLVMIdentifier*)realloc(ctx->IdList, ctx->IdCapacity * sizeof(LAN2LLVMIdentifier));
  }
}

void LAN2LLVMContext_ClearIdList(LAN2LLVMContext* ctx)
{
  ctx->IdCount = 0;
  ctx->IdCapacity = 4;
  free(ctx->IdList);
  ctx->IdList = NULL;
}

LAN2LLVMIdentifier* LAN2LLVMContext_RegisterNewId(LAN2LLVMContext* ctx, LANToken* id, LAN2LLVMType type)
{
  LAN2LLVMContext_GrowIdList(ctx);

  ctx->IdList[ctx->IdCount - 1] =
  type == LAN2LLVMFloat
  ? (LAN2LLVMIdentifier){
      .FloatVariants = 1,
      .IntVariants = 0,
      .Text = id->Text,
      .LastType = LAN2LLVMFloat,
    }
  : (LAN2LLVMIdentifier){
      .FloatVariants = 0,
      .IntVariants = 1,
      .Text = id->Text,
      .LastType = LAN2LLVMInt,
    };

  return ctx->IdList + ctx->IdCount - 1;
}

LAN2LLVMIdentifier* LAN2LLVMContext_RegisterId(LAN2LLVMContext* ctx, LANToken* id, LAN2LLVMType type)
{
  if (ctx->IdList == NULL)
  {
    ctx->IdList = (LAN2LLVMIdentifier*)malloc(sizeof(LAN2LLVMIdentifier) * ctx->IdCapacity);
  }

  for (size_t i = 0; i < ctx->IdCount; i++)
  {
    if (LANString_Equals(ctx->IdList[i].Text, id->Text))
    {
      if (type == LAN2LLVMInt)
      {
        ctx->IdList[i].IntVariants++;
      }
      else
      {
        ctx->IdList[i].FloatVariants++;
      }
      ctx->IdList[i].LastType = type;
      return ctx->IdList + i;
    }
  }

  return LAN2LLVMContext_RegisterNewId(ctx, id, type);
}

LAN2LLVMIdentifier* LAN2LLVMContext_GetId(LAN2LLVMContext* ctx, LANToken* id)
{
  if (ctx->IdList == NULL)
  {
    return NULL;
  }
  for (size_t i = 0; i < ctx->IdCount; i++)
  {
    if (LANString_Equals(ctx->IdList[i].Text, id->Text))
    {
      return ctx->IdList + i;
    }
  }
  return NULL;
}

LANAstInstr* LAN2LLVMContext_GetOuterFunc(LAN2LLVMContext* inst)
{
  return inst->OuterFunc;
}

void LAN2LLVMContext_SetOuterFunc(LAN2LLVMContext* inst, LANAstInstr* instr)
{
  inst->OuterFunc = instr;
}

LANAstInstr* LAN2LLVMContext_GetLastCalledFunc(LAN2LLVMContext* inst)
{
  return inst->LastCall;
}

void LAN2LLVMContext_SetLastCalledFunc(LAN2LLVMContext* inst, LANAstInstr* instr)
{
  inst->LastCall = instr;
}