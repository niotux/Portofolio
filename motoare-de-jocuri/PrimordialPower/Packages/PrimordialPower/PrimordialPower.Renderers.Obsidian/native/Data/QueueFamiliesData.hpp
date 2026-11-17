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
        struct QueueFamiliesData
        {
        public:
          uint32_t Count;
          uint32_t GraphicsFamilyIndex;
          uint32_t PresentFamilyIndex;
        };
      }
    }
  }
}