#pragma once

#include "lib/All.h"

#include "Data/ContextData.hpp"

namespace PrimordialPower
{
  namespace Renderers
  {
    namespace Obsidian
    {
      class RendererContext
      {
      private:
        Data::ContextData _data;

      public:
        RendererContext() = delete;
        RendererContext(const Data::ContextData &data);
        ~RendererContext();

        void DrawFrame();
        void RecreateSwapchain(uint32_t width, uint32_t height);

      private:
        void RecordCommandBuffer(VkCommandBuffer commandBuffer, uint32_t imageIndex);
        void CleanupSwapchain();
      };
    }
  }
}
