#include "ConvertFile.h"
#include "States.h"

#include "clikit.h"

#include <stdio.h>

const char* Ussage = "Usage: %s <source.lan> <output.ll>";

int main(int argc, char** argv)
{
  CLIState state = Start;
  CLICommand command = {
    .Output = NULL,
    .Source = NULL,
  };
  for (int i = 1; i < argc; i++)
  {
    state = ArgParserDelegations[state](argv[i], &command);
  }

  if (command.Source == NULL)
  {
    CLIKLog(CLIKError, "No source file path provided", Ussage, argv[0]);
    return 1;
  }

  if (command.Output == NULL)
  {
    CLIKLog(CLIKError, "No output file path provided", Ussage, argv[0]);
    return 1;
  }

  LAN2LLVMConvertFile(command.Source, command.Output);
}