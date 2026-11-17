#include "AstDebugging.h"
#include <stdio.h>

void LANPrintToken(const char* label, LANToken* tok)
{
  if (tok == NULL)
  {
    return;
  }
  fprintf(stdout, "    %12s %s;\n", label, LANString_Chars(tok->Text));
}

void LANPrintModule(LANAstModule* module)
{
  size_t count = LANAstModule_InstrCount(module);
  fprintf(stdout, "module[%zu]\n{\n", count);

  for (size_t i = 0; i < count; i++)
  {
    LANAstInstr* instr = LANAstModule_InstrAt(module, i);
    if (instr->Op == NULL)
    {
      fprintf(stdout, "  <null> // %zu\n  {\n", i);
    }
    else
    {
      fprintf(stdout, "  %s // %zu\n  {\n", LANString_Chars(instr->Op->Text), i);
    }
    LANPrintToken(".T0 = ", instr->TypeArg0);
    LANPrintToken(".T1 = ", instr->TypeArg1);
    LANPrintToken(".V0 = ", instr->ValueArg0);
    LANPrintToken(".V1 = ", instr->ValueArg1);
    LANPrintToken(".V2 = ", instr->ValueArg2);
    LANPrintToken(".V3 = ", instr->ValueArg3);
    fprintf(stdout, "  }\n");
  }
  fprintf(stdout, "}\n");
}