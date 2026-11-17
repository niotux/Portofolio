#pragma once
#include "Parser.h"
#include "Tokenizer.h"

typedef struct LANIdList LANIdList;

typedef enum
{
  Absent,
  Match,
  NotMatch,

} LANIdListStatus;

typedef struct
{
  LANIdListStatus Status;
  LANToken* Type;

} LANIdListResponse;

LANIdList* LANMakeIdList(LANAstModule* module);
void LANFreeIdList(LANIdList** instRef);

void LANIdList_RegisterFun(LANIdList* inst, LANAstInstr* fun);
void LANIdList_EnterFun(LANIdList* inst, LANAstInstr* fun);

LANIdListResponse LANIdList_CheckId(LANIdList* inst, LANToken* token, LANToken* type);
void LANIdList_AddId(LANIdList* inst, LANToken* name, LANToken* type);

void LANIdList_RegisterLabel(LANIdList* inst, LANToken* label);
int LANIdList_ResolveLabel(LANIdList* inst, LANToken* label);

LANIdListResponse LANIdList_CheckFun(LANIdList* inst, LANToken* name);
LANIdListResponse LANIdList_CheckArg(LANIdList* inst, size_t index);

LANIdListResponse LANIdList_CheckRetType(LANIdList* inst, LANToken* value);
const LANToken* LANIdList_GetCurrentFunName(LANIdList* inst);
