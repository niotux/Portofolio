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
        struct DeviceBundle
        {
        public:
          VkPhysicalDevice PhysicalDevice = VK_NULL_HANDLE;
          VkDevice Device = VK_NULL_HANDLE;
          VkQueue GraphicsQueue = VK_NULL_HANDLE;
          VkQueue PresentQueue = VK_NULL_HANDLE;
          uint32_t GraphicsQueueFamily = 0;
          uint32_t PresentQueueFamily = 0;
        };
      }
    }
  }
}
