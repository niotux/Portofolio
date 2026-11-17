#pragma once
#include "Tokenizer.h"

typedef struct LANAstModule LANAstModule;

typedef struct
{
  LANToken* Op;
  LANToken* TypeArg0;
  LANToken* TypeArg1;
  LANToken* ValueArg0;
  LANToken* ValueArg1;
  LANToken* ValueArg2;
  LANToken* ValueArg3;

} LANAstInstr;

LANAstModule* LANMakeModule(LANTokenArray* tokens);
void LANFreeModule(LANAstModule** instRef);

size_t LANAstModule_InstrCount(LANAstModule* inst);
LANAstInstr* LANAstModule_InstrAt(LANAstModule* inst, size_t index);
size_t LANAstModule_FuncCount(LANAstModule* inst);
LANAstInstr* LANAstModule_FuncAt(LANAstModule* inst, size_t index);
LANAstInstr* LANAstModule_GetFunc(LANAstModule* inst, LANToken* funcName);