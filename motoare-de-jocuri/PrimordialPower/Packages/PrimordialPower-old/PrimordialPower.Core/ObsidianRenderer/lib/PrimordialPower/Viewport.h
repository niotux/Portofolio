#pragma once

#include "Export.h"
#include "Syntax.h"

#include <cstdint>

namespace PrimordialPower
{
  enum class BackendType : uint8_t
  {
    Unknown = 0,
    Win32 = 1,
    X11 = 2,
    Wayland = 3,
    Cocoa = 4,
  };

  struct Win32WindowInfo
  {
    void *HInstance;
    void *HWnd;
  };

  struct X11WindowInfo
  {
    void *Display;
    uint64_t Window;
  };

  struct ViewportNative
  {
    BackendType Backend;
    union
    {
      Win32WindowInfo Win32;
      X11WindowInfo X11;
    };
  };

  struct ViewportData
  {
    uint32_t Width;
    uint32_t Height;
    int32_t XOffset;
    int32_t YOffset;
    float MinDepth;
    float MaxDepth;
    ViewportNative Native;
  };
}