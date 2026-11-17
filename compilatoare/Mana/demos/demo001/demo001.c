#include "Analyzer.h"
#include "AstDebugging.h"
#include "Parser.h"
#include "String.h"
#include "Tokenizer.h"

#include "clikit.h"

#include <stdio.h>
#include <stdlib.h>

int main(int argc, char** argv)
{
  if (argc < 2)
  {
    fprintf(stderr, "usage: %s <code>\n", argv[0]);
    return 1;
  }

  LANString* source = LANMakeString(argv[1]);
  if (source == NULL)
  {
    fprintf(stderr, "error: failed to allocate source string\n");
    return 1;
  }
  CLIKLog(CLIKInfo, "Made string", NULL);

  // LANFlushLastLog();

  LANTokenArray* tokens = LANTokenizeSource(source);
  if (tokens == NULL)
  {
    fprintf(stderr, "error: failed to tokenize source\n");
    LANFreeString(&source);
    return 1;
  }
  LANInjectMain(&tokens);
  CLIKLog(CLIKInfo, "Made tokens", NULL);

  // LANFlushLastLog();

  for (size_t i = 0; i < LANTokenArray_Count(tokens); i++)
  {
    LANToken* tok = LANTokenArray_At(tokens, i);
    printf("%zu:%zu\t%-24s\t%zu\t%s\n", tok->Line, tok->Char, LANTokenKind_Name(tok->Kind), tok->DataBuffer, LANString_Chars(tok->Text));
  }
  CLIKLog(CLIKInfo, "Printed tokens", NULL);

  LANAstModule* module = LANMakeModule(tokens);
  if (module == NULL)
  {
    fprintf(stderr, "error: failed to build AST\n");
    LANFreeTokens(&tokens);
    LANFreeString(&source);
    return 1;
  }
  CLIKLog(CLIKInfo, "Made module", NULL);
  // LANFlushLastLog();

  printf("%ld\n", LANAstModule_InstrCount(module));
  LANPrintModule(module);

  CLIKLog(CLIKInfo, "Printed module", NULL);

  LANAnalyzeModule(module);

  CLIKLog(CLIKInfo, "Analyzed module", NULL);

  if (LANGetMaxLoggedLevel() >= LANLogLevel_Warning)
  {
    LANFreeTokens(&tokens);
    LANFreeString(&source);
    LANFreeModule(&module);
    return 1;
  }

  LANFreeTokens(&tokens);
  LANFreeString(&source);
  return 0;
}