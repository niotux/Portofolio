#pragma once

#ifdef _WIN32
#define EXPORT extern "C" __declspec(dllexport)
#else
#define EXPORT extern "C" __attribute__((visibility("default")))
#endif

#include <cstdint>

// Mirror the C# ViewportData structure
struct ViewportData
{
  uint32_t Width;
  uint32_t Height;
  int32_t XOffset;
  int32_t YOffset;
  float MinDepth;
  float MaxDepth;

  struct Native
  {
    int32_t PlatformOS; // 0=Unknown, 1=Win32, 2=X11, 3=Cocoa

    struct Win32Info
    {
      void *HInstance;
      void *HWnd;
    } Win32;

    struct X11Data
    {
      void *Display;
      uint64_t Window;
    } X11;
  } Native;
};

// Exported functions
EXPORT bool InitVulkan(ViewportData *viewportData);
EXPORT void Render();
EXPORT void Cleanup();
EXPORT void ResizeViewport(uint32_t width, uint32_t height);
