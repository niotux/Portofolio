#pragma once

#include "RendererContext.hpp"
#include "Data/ApplicationData.hpp"
#include "Data/ViewportData.hpp"
#include "Data/DeviceBundle.hpp"
#include "Data/ContextData.hpp"

#include "lib/All.h"

namespace PrimordialPower
{
  namespace Renderers
  {
    namespace Obsidian
    {
      class Renderer
      {
      private:
        VkInstance _instance = VK_NULL_HANDLE;
        Data::ApplicationData _app{};
        std::vector<Data::DeviceBundle *> _deviceBundles;

      public:
        Renderer();
        ~Renderer();

        void Init(const Data::ApplicationData &app);
        void CreateContext(RendererContext *context_out, const Data::ViewportData &viewport);

      private:
        // Instance setup
        void VulkanCreateInstance(const Data::ApplicationData &app);

        // Surface creation (per viewport)
        VkSurfaceKHR VulkanCreateSurface(const Data::ViewportData &viewport);

        // Lazy device management
        Data::DeviceBundle *FindOrCreateDevice(VkSurfaceKHR surface);
        Data::DeviceBundle *PickAndCreateDevice(VkSurfaceKHR surface);

        // Context setup helpers (operate on ContextData, not Renderer state)
        void CreateSwapchain(Data::ContextData &ctx, const Data::ViewportData &viewport);
        void CreateImageViews(Data::ContextData &ctx);
        void CreateRenderPass(Data::ContextData &ctx);
        void CreateFramebuffers(Data::ContextData &ctx);
        void CreateCommandPool(Data::ContextData &ctx);
        void CreateCommandBuffer(Data::ContextData &ctx);
        void CreateSyncObjects(Data::ContextData &ctx);
      };
    }
  }
}
