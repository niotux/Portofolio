#include "Translator.h"

#include "Context.h"
#include "Emitter.h"

// Literally nothing here

LANString* LAN2LLVMTranslateSource(LANString* source)
{
  LANTokenArray* tokens = LANTokenizeSource(source);
  if (tokens == NULL)
  {
    return NULL;
  }

  LANAstModule* module = LANMakeModule(tokens);
  if (module == NULL)
  {
    return NULL;
  }

  LANAnalyzeModule(module);
  if (LANGetMaxLoggedLevel() > LANLogLevel_Warning)
  {
    LANFreeModule(&module);
    return NULL;
  }

  size_t length = 0;
  LANString* result = NULL;

  LAN2LLVMContext* dummy = LAN2LLVMMakeContext(NULL);
  {
    LAN2LLVMEmmitModule(dummy, module);
    length = LAN2LLVMContext_Cursor(dummy);

    result = LANMakeStringOfLength(length);

    LAN2LLVMContext* ctx = LAN2LLVMMakeContext(result);
    {
      LAN2LLVMEmmitModule(ctx, module);
      LAN2LLVMFreeContext(&ctx);
    }
    LAN2LLVMFreeContext(&dummy);
  }
  LANFreeModule(&module);
  return result;
}