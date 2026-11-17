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
        struct ApplicationData
        {
        public:
          uint32_t AppVersion;
          uint32_t EngineVersion;
          uint32_t VkApiVersion;
          const char *AppName;
          const char *EngineName;
        };
      }
    }
  }
}