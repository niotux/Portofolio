#include "Analyzer.h"
#include "AnalyzerRules.h"

typedef void (*InstrAnalyzerFunc)(Analyzer* az, LANAstInstr* instr);

static const InstrAnalyzerFunc AnalyzerDelegations[] = {
  [LANTokenKind_Op_LdArg] = Analyze_LdArg,
  [LANTokenKind_Op_Set] = Analyze_Set,

  [LANTokenKind_Op_Add] = Analyze_BinOperation,
  [LANTokenKind_Op_Sub] = Analyze_BinOperation,
  [LANTokenKind_Op_Mul] = Analyze_BinOperation,
  [LANTokenKind_Op_Div] = Analyze_BinOperation,
  [LANTokenKind_Op_Mod] = Analyze_BinOperation,

  [LANTokenKind_Op_IDiv] = Analyze_BinOperation,
  [LANTokenKind_Op_IMod] = Analyze_BinOperation,

  [LANTokenKind_Op_FAdd] = Analyze_BinOperation,
  [LANTokenKind_Op_FSub] = Analyze_BinOperation,
  [LANTokenKind_Op_FMul] = Analyze_BinOperation,
  [LANTokenKind_Op_FDiv] = Analyze_BinOperation,
  [LANTokenKind_Op_FMod] = Analyze_BinOperation,

  [LANTokenKind_Op_Or] = Analyze_BinOperation,
  [LANTokenKind_Op_And] = Analyze_BinOperation,
  [LANTokenKind_Op_Xor] = Analyze_BinOperation,
  [LANTokenKind_Op_ShL] = Analyze_BinOperation,
  [LANTokenKind_Op_ShR] = Analyze_BinOperation,
  [LANTokenKind_Op_IShR] = Analyze_BinOperation,
  [LANTokenKind_Op_Not] = Analyze_UnOperation,

  [LANTokenKind_Op_Alloc] = Analyze_AdressDestOperation,
  [LANTokenKind_Op_Write] = Analyze_AdressDestOperation,
  [LANTokenKind_Op_Read] = Analyze_AdressSrcOperation,
  [LANTokenKind_Op_Gep] = Analyze_Gep,

  [LANTokenKind_Op_Conv] = Analyze_Conversion,
  [LANTokenKind_Op_IConv] = Analyze_Conversion,
  [LANTokenKind_Op_FConv] = Analyze_Conversion,
  [LANTokenKind_Op_IToF] = Analyze_Conversion,
  [LANTokenKind_Op_UToF] = Analyze_Conversion,
  [LANTokenKind_Op_FToI] = Analyze_Conversion,
  [LANTokenKind_Op_FToU] = Analyze_Conversion,

  [LANTokenKind_Op_Label] = Analyze_Label,
  [LANTokenKind_Op_Jmp] = Analyze_Jmp,
  [LANTokenKind_Op_Jeq] = Analyze_JumpCompare,
  [LANTokenKind_Op_Jne] = Analyze_JumpCompare,
  [LANTokenKind_Op_Jlt] = Analyze_JumpCompare,
  [LANTokenKind_Op_Jgt] = Analyze_JumpCompare,
  [LANTokenKind_Op_Jle] = Analyze_JumpCompare,
  [LANTokenKind_Op_Jge] = Analyze_JumpCompare,
  [LANTokenKind_Op_IJlt] = Analyze_JumpCompare,
  [LANTokenKind_Op_IJgt] = Analyze_JumpCompare,
  [LANTokenKind_Op_IJle] = Analyze_JumpCompare,
  [LANTokenKind_Op_IJge] = Analyze_JumpCompare,
  [LANTokenKind_Op_FJlt] = Analyze_JumpCompare,
  [LANTokenKind_Op_FJgt] = Analyze_JumpCompare,
  [LANTokenKind_Op_FJle] = Analyze_JumpCompare,
  [LANTokenKind_Op_FJge] = Analyze_JumpCompare,

  [LANTokenKind_Op_Ret] = Analyze_Ret,

  [LANTokenKind_Op_Fun] = Analyze_Fun,
  [LANTokenKind_Op_Call] = Analyze_Call,
  [LANTokenKind_Op_Arg] = Analyze_Arg,
};

void LANAnalyzeModule(LANAstModule* module)
{
  Analyzer az = {
    .Ids = LANMakeIdList(module),
    .Module = module,
    .Cursor = 0,
  };

  size_t count = LANAstModule_InstrCount(module);

  for (size_t i = 0; i < count; i++)
  {
    LANAstInstr* instr = LANAstModule_InstrAt(module, i);
    if (instr->Op->Kind == LANTokenKind_Op_Fun)
    {
      LANIdList_RegisterFun(az.Ids, instr);
    }
  }

  az.Cursor = 0;
  while (az.Cursor < count)
  {
    LANAstInstr* instr = LANAstModule_InstrAt(module, az.Cursor);
    az.Cursor++;
    AnalyzerDelegations[instr->Op->Kind](&az, instr);
  }

  LANFreeIdList(&az.Ids);
}
