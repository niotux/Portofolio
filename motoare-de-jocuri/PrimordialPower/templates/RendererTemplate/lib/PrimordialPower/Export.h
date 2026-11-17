#pragma once

#ifdef _WIN32
#define EXPORT extern "C" __declspec(dllexport)
#else
#define EXPORT extern "C" __attribute__((visibility("default")))
#endif

#define EXPORT_CLASS_New(ClassName, ExportName) \
  EXPORT ClassName *ExportName##_New()          \
  {                                             \
    return new ClassName();                     \
  }

#define EXPORT_CLASS_Dispose(ClassName, ExportName)   \
  EXPORT void ExportName##_Dispose(ClassName *handle) \
  {                                                   \
    delete handle;                                    \
  }
