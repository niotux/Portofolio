#pragma once
#include "lib/All.h"

namespace PrimordialPower
{
  namespace Renderers
  {
    namespace Obsidian
    {
      namespace Data
      {
        namespace Linux
        {
          struct X11WindowHandle
          {
          public:
            intptr_t Display;
            int64_t Window;
          };
        }

        namespace Windows
        {
          struct Win32WindowHandle
          {
          public:
            intptr_t HInstance;
            intptr_t HWnd;
          };
        }

        // Matches C# PrimordialPower.Platform.ViewportHandle layout exactly:
        //   [StructLayout(LayoutKind.Explicit, Size = sizeof(BackendType) + HandleStructSize)]
        //   BackendType (1 byte) at offset 0, handle union at offset 1, total = 129 bytes
        enum class BackendType : uint8_t
        {
          Unknown = 0,
          Win32 = 1,
          X11 = 2,
          Wayland = 3,
          Cocos = 4,
        };

#pragma pack(push, 1)
        struct ViewportHandle
        {
        public:
          static constexpr int HandleStructSize = 128;

          BackendType Backend = BackendType::Unknown;
          union
          {
            Linux::X11WindowHandle X11;
            Windows::Win32WindowHandle Win32;
            uint8_t _raw[HandleStructSize]; // ensures 128-byte union area
          };
        };
#pragma pack(pop)

        struct ViewportData
        {
        public:
          uint32_t Width;
          uint32_t Height;
          int32_t OffsetX;
          int32_t OffsetY;
          ViewportHandle Handle;
        };
      }
    }
  }
}
