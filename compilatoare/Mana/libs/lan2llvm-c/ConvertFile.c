#include "ConvertFile.h"
#include "Translator.h"

#include "clikit.h"

#include <stdio.h>

void LAN2LLVMConvertFile(const char* sourceFilePath, const char* outFilePath)
{
  FILE* srcFile = fopen(sourceFilePath, "r");
  if (srcFile == NULL)
  {
    CLIKLog(CLIKError, "The source file %s cannot be opened.", "It doesn't exist or permissions are denied", sourceFilePath);
    return;
  }

  fseek(srcFile, 0, SEEK_END);
  size_t size = (size_t)ftell(srcFile);
  rewind(srcFile);

  size_t _ = 0x0;

  LANString* source = LANMakeStringOfLength(size);
  _ = fread(LANString_Chars(source), 1, LANString_Count(source), srcFile);

  LANString* output = LAN2LLVMTranslateSource(source);
  if (output == NULL)
  {
    return;
  }

  FILE* outFile = fopen(outFilePath, "w");
  if (outFile == NULL)
  {
    CLIKLog(CLIKError, "The output file %s cannot be opened.", "It doesn't exist or permissions are denied", outFilePath);
    return;
  }
  _ = fwrite(LANString_Chars(output), 1, LANString_Count(output), outFile);

  (void)_;

  LANFreeString(&output);
  LANFreeString(&source);

  fclose(outFile);
  outFile = NULL;
  fclose(srcFile);
  srcFile = NULL;
}