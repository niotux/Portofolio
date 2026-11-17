#include "Logging.h"
#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>

static const char* Format = "%s %s \033[90m[%s]\033[0m\n";
static char* Prefixes[] = {
  [CLIKError] = "\033[90m[ \033[31mError\033[90m ]\033[0m",
  [CLIKWarning] = "\033[90m[ \033[33mWarn\033[90m ]\033[0m",
  [CLIKInfo] = "\033[90m[ \033[32mInfo\033[90m ]\033[0m",
  [CLIKUnknown] = "\033[90m[ \033[34m???\033[90m ]\033[0m"
};

static const int MaxStackSize = 1024;

void CLIKLog(CLIKLogLevel level, const char* fmtMessage, const char* fmtDetails, ...)
{
  if (level > CLIKError || level < 0)
  {
    level = CLIKUnknown;
  }
  char* prefix = Prefixes[level];

  if (fmtMessage == NULL)
  {
    fmtMessage = "";
  }
  if (fmtDetails == NULL)
  {
    fmtDetails = "";
  }

  int size = snprintf(NULL, 0, Format, prefix, fmtMessage, fmtDetails) + 1;
  if (size <= MaxStackSize)
  {
    char buffer[MaxStackSize];
    snprintf(buffer, size, Format, prefix, fmtMessage, fmtDetails);
    va_list args;
    va_start(args, fmtDetails);
    vprintf(buffer, args);
    va_end(args);
  }
  else
  {
    char* buffer = malloc(size);
    if (buffer == NULL)
    {
      return;
    }
    snprintf(buffer, size, prefix, Format, fmtMessage, fmtDetails);
    va_list args;
    va_start(args, fmtDetails);
    vprintf(buffer, args);
    va_end(args);
    free(buffer);
  }
}