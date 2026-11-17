#pragma once
#include <stdlib.h>
#include <string.h>

typedef struct
{
  char* Source;
  char* Output;

} CLICommand;

typedef enum
{
  Start,
  ParsedSource,
  ParsedOutput,

} CLIState;

CLIState ParseFrom_Start(char* nextArg, CLICommand* com)
{
  com->Source = nextArg;
  return ParsedSource;
}

CLIState ParseFrom_Source(char* nextArg, CLICommand* com)
{
  com->Output = nextArg;
  return ParsedOutput;
}

typedef CLIState (*ArgParserFunc)(char* nextArg, CLICommand* com);

ArgParserFunc ArgParserDelegations[] = {
  [Start] = ParseFrom_Start,
  [ParsedSource] = ParseFrom_Source,
};