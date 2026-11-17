#pragma once
#include "lib/All.h"

#include "DeviceBundle.hpp"

namespace PrimordialPower
{
  namespace Renderers
  {
    namespace Obsidian
    {
      namespace Data
      {
        struct ContextData
        {
        public:
          // Presentation
          VkSurfaceKHR Surface = VK_NULL_HANDLE;
          VkSwapchainKHR Swapchain = VK_NULL_HANDLE;
          std::vector<VkImage> Images;
          std::vector<VkImageView> ImageViews;
          VkFormat Format = VK_FORMAT_UNDEFINED;
          VkExtent2D Extent = {0, 0};

          // Render pass + framebuffers
          VkRenderPass RenderPass = VK_NULL_HANDLE;
          std::vector<VkFramebuffer> Framebuffers;

          // Commands
          VkCommandPool CommandPool = VK_NULL_HANDLE;
          VkCommandBuffer CommandBuffer = VK_NULL_HANDLE;

          // Synchronization
          VkSemaphore ImageAvailableSemaphore = VK_NULL_HANDLE;
          VkSemaphore RenderFinishedSemaphore = VK_NULL_HANDLE;
          VkFence InFlightFence = VK_NULL_HANDLE;

          // Back-references for cleanup and API calls
          DeviceBundle *Device = nullptr;
          VkInstance Instance = VK_NULL_HANDLE;
        };
      }
    }
  }
}
