#pragma once

typedef enum
{
  CLIKUnknown,
  CLIKInfo,
  CLIKWarning,
  CLIKError,

} CLIKLogLevel;

void CLIKLog(CLIKLogLevel level, const char* fMessage, const char* fDetails, ...);