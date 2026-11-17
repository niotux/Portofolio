#include "Log.h"
#include "List.h"

#include "clikit.h"

#include <stdio.h>

#define TokenF "\033[35m'%s'\033[0m"

static void LogToCLIK(CLIKLogLevel level, const char* message, const LANToken* context, const LANToken* fault)
{
  if (message == NULL)
  {
    return;
  }
  if (context != NULL && fault != NULL)
  {
    CLIKLog(level, "%s " TokenF ": " TokenF, "Line %zu, Char %zu", message, LANString_Chars(context->Text), LANString_Chars(fault->Text), fault->Line, fault->Char);
    return;
  }
  if (context == NULL && fault != NULL)
  {
    CLIKLog(level, "%s: " TokenF, "Line %zu, Char %zu", message, LANString_Chars(fault->Text), fault->Line, fault->Char);
    return;
  }
  if (context == NULL && fault == NULL)
  {
    CLIKLog(level, "%s", NULL, message);
    return;
  }
  if (context != NULL && fault == NULL)
  {
    CLIKLog(level, "%s " TokenF, "Line %zu, Char %zu", message, LANString_Chars(context->Text), context->Line, context->Char);
    return;
  }
}

// static LANLogMessage* LastMsg = NULL;
// LANLogMessage* LANGetLastLog()
// {
//   return LastMsg;
// }

// void LANFlushLastLog()
// {
//   free(LastMsg);
//   LastMsg = NULL;
// }

static LANLogLevel WorstLevel = LANLogLevel_None;
LANLogLevel LANGetMaxLoggedLevel()
{
  return WorstLevel;
}

LANList* CallbackList = NULL;
void LANLogAddCallback(LANLogCallback callback)
{
  if (CallbackList == NULL)
  {
    CallbackList = LANMakeList();
  }
  LANList_Push(CallbackList, callback);
}
void LANLogClearCallbacks()
{
  LANList_Clear(CallbackList);
}

void LANLogContext(const LANLogMessage* message, const LANToken* context, const LANToken* fault)
{
  // if (LastMsg == NULL)
  // {
  //   LastMsg = malloc(sizeof(LANLogMessage));
  // }
  // memcpy(LastMsg, (uint8_t*)message, sizeof(LANLogMessage));

  if (message->Level > WorstLevel)
  {
    WorstLevel = message->Level;
  }

  if (CallbackList != NULL)
  {
    LANLogCallbackResponse resp = LANLogCallbackResponse_PrintLogToStdout;
    for (size_t i = 0; i < LANList_Count(CallbackList); i++)
    {
      resp = ((LANLogCallback)LANList_At(CallbackList, i))(message, context, fault);
      if (resp == LANLogCallbackResponse_StopPropagating)
      {
        break;
      }
    }

    if (resp != LANLogCallbackResponse_PrintLogToStdout)
    {
      return;
    }
  }

  switch (message->Level)
  {
  case LANLogLevel_Error:
    LogToCLIK(CLIKError, message->Message, context, fault);
    break;

  case LANLogLevel_Warning:
    LogToCLIK(CLIKWarning, message->Message, context, fault);
    break;

  case LANLogLevel_Info:
    LogToCLIK(CLIKInfo, message->Message, context, fault);
    break;

  default:
    LogToCLIK(CLIKUnknown, message->Message, context, fault);
    break;
  }
}

void LANLog(const LANLogMessage* message, const LANToken* fault)
{
  LANLogContext(message, NULL, fault);
}