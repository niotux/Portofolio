#include "Parser.h"
#include "List.h"
#include "NullChecks.h"
#include "ParserStages.h"

#include <stdint.h>
#include <stdio.h>

struct LANAstModule
{
  LANTokenArray* Tokens; // owned: the AST borrows LANToken* into this array
  LANList* FuncDeclInstructions;
  LANAstInstr* Instructions;
  size_t InstrCount;
};

// Puha is a time traveller.
static void ParseTokens(LANTokenArray* tokens, LANAstModule* dest)
{
  ParserStage state = ParsedInstrEnd;

  size_t instrCount = 0;
  LANToken* currTok = NULL;
  for (size_t i = 0; i < LANTokenArray_Count(tokens); i++)
  {
    currTok = LANTokenArray_At(tokens, i);
    if (state == InstrFatal)
    {
      if (!LANTokenKind_IsOp(currTok->Kind))
      {
        continue;
      }
      state = ParsedInstrEnd;
    }
    if (LANTokenKind_IsOp(currTok->Kind))
    {
      instrCount++;
    }
    if (instrCount > dest->InstrCount)
    {
      LANLog(&LANMsg_Fatal_Program_OutOfBounds, currTok);
      return;
    }
    state = ParserStagesDelegation[state](currTok, &dest->Instructions[instrCount - 1]);
    LANAstInstr* instr = &dest->Instructions[instrCount - 1];
    if (instr->Op->Kind == LANTokenKind_Op_Fun)
    {
      LANList_Push(dest->FuncDeclInstructions, instr);
    }
  }
}

LANAstModule* LANMakeModule(LANTokenArray* tokens)
{
  LANInjectMain(&tokens);

  LANAstModule* module = malloc(sizeof(LANAstModule));
  EXIT_ON_NULL(module)

  // The module owns the (injected) token array; its instructions borrow
  // LANToken* into it, so it must live exactly as long as the module.
  module->Tokens = tokens;

  size_t instrCount = 0;
  for (size_t i = 0; i < LANTokenArray_Count(tokens); i++)
  {
    LANToken* token = LANTokenArray_At(tokens, i);
    if (LANTokenKind_IsOp(token->Kind))
    {
      instrCount++;
    }
  }

  module->InstrCount = instrCount;
  module->Instructions = calloc(instrCount, sizeof(LANAstInstr));
  module->FuncDeclInstructions = LANMakeList();
  EXIT_ON_NULL(module->Instructions);

  ParseTokens(tokens, module);

  return module;
}

void LANFreeModule(LANAstModule** instRef)
{
  EXIT_ON_NULL_VOID(instRef);
  EXIT_ON_NULL_VOID(*instRef);
  LANAstModule* inst = *instRef;
  free(inst->Instructions);
  LANFreeTokens(&inst->Tokens);
  LANFreeList(&inst->FuncDeclInstructions);
  free(inst);
  *instRef = NULL;
}

size_t LANAstModule_InstrCount(LANAstModule* inst)
{
  EXIT_ON_NULL_INT(inst);
  return inst->InstrCount;
}

LANAstInstr* LANAstModule_InstrAt(LANAstModule* inst, size_t index)
{
  EXIT_ON_NULL(inst);
  return inst->Instructions + index;
}

size_t LANAstModule_FuncCount(LANAstModule* inst)
{
  return LANList_Count(inst->FuncDeclInstructions);
}

LANAstInstr* LANAstModule_FuncAt(LANAstModule* inst, size_t index)
{
  return LANList_At(inst->FuncDeclInstructions, index);
}

LANAstInstr* LANAstModule_GetFunc(LANAstModule* inst, LANToken* funcName)
{
  for (size_t i = 0; i < LANList_Count(inst->FuncDeclInstructions); i++)
  {
    LANAstInstr* instr = LANList_At(inst->FuncDeclInstructions, i);
    if (LANString_Equals(instr->ValueArg0->Text, funcName->Text))
    {
      return instr;
    }
  }
  return NULL;
}