#pragma once
#include "Tokenizer.h"
#include "stdlib.h"

typedef enum
{
  LANLogLevel_None,
  LANLogLevel_Info,
  LANLogLevel_Warning,
  LANLogLevel_Error,
  LANLogLevel_Fatal,

} LANLogLevel;

typedef struct
{
  const LANLogLevel Level;
  const char* Message;

} LANLogMessage;

typedef enum
{
  LANLogCallbackResponse_PrintLogToStdout,
  LANLogCallbackResponse_OnlyPropagateToCallbacks,
  LANLogCallbackResponse_StopPropagating,

} LANLogCallbackResponse;

typedef LANLogCallbackResponse (*LANLogCallback)(const LANLogMessage* message, const LANToken* context, const LANToken* fault);

// LANLogMessage* LANGetLastLog();
// void LANFlushLastLog();
LANLogLevel LANGetMaxLoggedLevel();
void LANLogContext(const LANLogMessage* message, const LANToken* context, const LANToken* fault);
void LANLog(const LANLogMessage* message, const LANToken* token);
void LANLogAddCallback(LANLogCallback callback);
void LANLogClearCallbacks();