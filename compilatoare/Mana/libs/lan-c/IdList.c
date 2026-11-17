#include "IdList.h"
#include "List.h"
#include "NullChecks.h"
#include "String.h"
#include "Tokenizer.h"

#include <stdlib.h>

struct LANIdList
{
  LANAstModule* Module;
  LANList* Funs;
  LANList* Ids;
  LANList* Labels;
  LANAstInstr* CurrentFun;
  LANAstInstr* CurrentCall;
};

// Associates a declared identifier with its type token. Both tokens are borrowed
// from the module's token array; the entry itself is owned by the Ids list.
typedef struct
{
  LANToken* Name;
  LANToken* Type;

} LANIdEntry;

static LANIdEntry* MakeIdEntry(LANToken* name, LANToken* type)
{
  LANIdEntry* entry = malloc(sizeof(LANIdEntry));
  EXIT_ON_NULL(entry);

  entry->Name = name;
  entry->Type = type;
  return entry;
}

static LANAstInstr* ModuleEnd(LANIdList* inst)
{
  return LANAstModule_InstrAt(inst->Module, 0) + LANAstModule_InstrCount(inst->Module);
}

LANIdList* LANMakeIdList(LANAstModule* module)
{
  LANIdList* inst = malloc(sizeof(LANIdList));
  EXIT_ON_NULL(inst);

  inst->Module = module;
  inst->Funs = LANMakeList();
  inst->Ids = LANMakeList();
  inst->Labels = LANMakeList();
  inst->CurrentFun = NULL;
  inst->CurrentCall = NULL;
  return inst;
}

void LANFreeIdList(LANIdList** instRef)
{
  EXIT_ON_NULL_VOID(instRef);
  EXIT_ON_NULL_VOID(*instRef);

  LANIdList* inst = *instRef;
  LANList_FreeElements(inst->Ids);
  LANFreeList(&inst->Funs);
  LANFreeList(&inst->Ids);
  LANFreeList(&inst->Labels);
  free(inst);
  *instRef = NULL;
}

void LANIdList_RegisterFun(LANIdList* inst, LANAstInstr* fun)
{
  LANList_Push(inst->Funs, fun);
}

void LANIdList_EnterFun(LANIdList* inst, LANAstInstr* fun)
{
  inst->CurrentFun = fun;
  LANList_FreeElements(inst->Ids);
  LANList_Clear(inst->Ids);
  LANList_Clear(inst->Labels);

  LANAstInstr* end = ModuleEnd(inst);
  for (LANAstInstr* p = fun + 1; p < end; p++)
  {
    if (p->Op->Kind == LANTokenKind_Op_Fun)
    {
      break;
    }
    if (p->Op->Kind == LANTokenKind_Op_Label)
    {
      LANIdList_RegisterLabel(inst, p->ValueArg0);
    }
  }
}

LANIdListResponse LANIdList_CheckId(LANIdList* inst, LANToken* token, LANToken* type)
{
  if (token == NULL)
  {
    return (LANIdListResponse){ .Status = Match, .Type = type };
  }

  // Literals carry no declared type: number literals fit any type, while string
  // literals must match the type by size. Anything that is neither a string
  // literal nor an identifier is treated as a number-like literal.
  if (token->Kind != LANTokenKind_Val_Identifier)
  {
    if (token->Kind == LANTokenKind_Val_StringLiteral)
    {
      if (LANToken_EqualsData(token, type))
      {
        return (LANIdListResponse){ .Status = Match, .Type = type };
      }
      return (LANIdListResponse){ .Status = NotMatch, .Type = type };
    }
    return (LANIdListResponse){ .Status = Match, .Type = type };
  }

  size_t count = LANList_Count(inst->Ids);
  for (size_t i = 0; i < count; i++)
  {
    LANIdEntry* entry = LANList_At(inst->Ids, i);
    if (!LANString_Equals(entry->Name->Text, token->Text))
    {
      continue;
    }
    if (LANToken_EqualsData(entry->Type, type))
    {
      return (LANIdListResponse){ .Status = Match, .Type = entry->Type };
    }
    return (LANIdListResponse){ .Status = NotMatch, .Type = entry->Type };
  }

  return (LANIdListResponse){ .Status = Absent, .Type = NULL };
}

void LANIdList_AddId(LANIdList* inst, LANToken* name, LANToken* type)
{
  LANList_Push(inst->Ids, MakeIdEntry(name, type));
}

void LANIdList_RegisterLabel(LANIdList* inst, LANToken* label)
{
  LANList_Push(inst->Labels, label);
}

int LANIdList_ResolveLabel(LANIdList* inst, LANToken* label)
{
  size_t count = LANList_Count(inst->Labels);
  for (size_t i = 0; i < count; i++)
  {
    LANToken* entry = LANList_At(inst->Labels, i);
    if (LANString_Equals(entry->Text, label->Text))
    {
      return 1;
    }
  }
  return 0;
}

LANIdListResponse LANIdList_CheckFun(LANIdList* inst, LANToken* name)
{
  size_t count = LANList_Count(inst->Funs);
  for (size_t i = 0; i < count; i++)
  {
    LANAstInstr* fun = LANList_At(inst->Funs, i);
    if (!LANString_Equals(fun->ValueArg0->Text, name->Text))
    {
      continue;
    }

    inst->CurrentCall = fun;
    return (LANIdListResponse){ .Status = Match, .Type = fun->TypeArg0 };
  }
  return (LANIdListResponse){ .Status = Absent, .Type = NULL };
}

LANIdListResponse LANIdList_CheckArg(LANIdList* inst, size_t index)
{
  LANAstInstr* end = ModuleEnd(inst);
  LANAstInstr* param = inst->CurrentCall + 1;

  for (size_t k = 0; k < index; k++)
  {
    if (param >= end || param->Op->Kind != LANTokenKind_Op_LdArg)
    {
      return (LANIdListResponse){ .Status = Absent, .Type = NULL };
    }
    param++;
  }

  if (param >= end || param->Op->Kind != LANTokenKind_Op_LdArg)
  {
    return (LANIdListResponse){ .Status = Absent, .Type = NULL };
  }

  return (LANIdListResponse){ .Status = Match, .Type = param->TypeArg0 };
}

LANIdListResponse LANIdList_CheckRetType(LANIdList* inst, LANToken* value)
{
  return LANIdList_CheckId(inst, value, inst->CurrentFun->TypeArg0);
}

const LANToken* LANIdList_GetCurrentFunName(LANIdList* inst)
{
  if (inst->CurrentFun == NULL)
  {
    return NULL;
  }
  return inst->CurrentFun->ValueArg0;
}
