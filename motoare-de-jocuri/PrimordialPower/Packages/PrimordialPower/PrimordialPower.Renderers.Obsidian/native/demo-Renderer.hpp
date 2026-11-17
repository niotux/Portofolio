#pragma once

#include <stdio.h>
#include <vector>
#include <string>

#include "lib/Export.h"

#if defined(_WIN32)
#define VK_USE_PLATFORM_WIN32_KHR

#elif defined(__linux__)
#define VK_USE_PLATFORM_XLIB_KHR

#elif defined(__APPLE__)
#define VK_USE_PLATFORM_METAL_EXT

#endif

#include "vulkan/vulkan.h"

namespace PrimordialPower
{
  namespace Renderers
  {
    namespace Obsidian
    {
      class RendererDemo
      {
      public:
        RendererDemo();
        ~RendererDemo();

        void Init(intptr_t display, uint64_t window);
        void LoadShaders(const char *vertShadPath, const char *fragShadPath);
        void LoadMesh(float *vertices, uint32_t vertexCount, uint32_t *indices, uint32_t indexCount);
        void Render();

      private:
        void CreateInstance();
        void CreateSurface(intptr_t display, uint64_t window);
        void PickPhysicalDevice();
        void CreateLogicalDevice();
        void CreateSwapchain();
        void CreateImageViews();
        void CreateRenderPass();
        void CreateGraphicsPipeline();
        void CreateFramebuffers();
        void CreateCommandPool();
        void CreateCommandBuffer();
        void CreateSyncObjects();
        void CreateVertexBuffer();
        void CreateIndexBuffer();
        void RecordCommandBuffer(VkCommandBuffer commandBuffer, uint32_t imageIndex);
        VkShaderModule CreateShaderModule(const std::vector<char> &code);
        uint32_t FindMemoryType(uint32_t typeFilter, VkMemoryPropertyFlags properties);
        void CreateBuffer(VkDeviceSize size, VkBufferUsageFlags usage, VkMemoryPropertyFlags properties, VkBuffer &buffer, VkDeviceMemory &bufferMemory);
        void CopyBuffer(VkBuffer srcBuffer, VkBuffer dstBuffer, VkDeviceSize size);
        void Cleanup();

        VkInstance m_instance = VK_NULL_HANDLE;
        VkSurfaceKHR m_surface = VK_NULL_HANDLE;
        VkPhysicalDevice m_physicalDevice = VK_NULL_HANDLE;
        VkDevice m_device = VK_NULL_HANDLE;
        VkQueue m_graphicsQueue = VK_NULL_HANDLE;
        VkQueue m_presentQueue = VK_NULL_HANDLE;
        uint32_t m_graphicsQueueFamily = 0;
        uint32_t m_presentQueueFamily = 0;

        VkSwapchainKHR m_swapchain = VK_NULL_HANDLE;
        std::vector<VkImage> m_swapchainImages;
        std::vector<VkImageView> m_swapchainImageViews;
        VkFormat m_swapchainImageFormat;
        VkExtent2D m_swapchainExtent;

        VkRenderPass m_renderPass = VK_NULL_HANDLE;
        VkPipelineLayout m_pipelineLayout = VK_NULL_HANDLE;
        VkPipeline m_graphicsPipeline = VK_NULL_HANDLE;

        std::vector<VkFramebuffer> m_framebuffers;
        VkCommandPool m_commandPool = VK_NULL_HANDLE;
        VkCommandBuffer m_commandBuffer = VK_NULL_HANDLE;

        VkSemaphore m_imageAvailableSemaphore = VK_NULL_HANDLE;
        VkSemaphore m_renderFinishedSemaphore = VK_NULL_HANDLE;
        VkFence m_inFlightFence = VK_NULL_HANDLE;

        VkBuffer m_vertexBuffer = VK_NULL_HANDLE;
        VkDeviceMemory m_vertexBufferMemory = VK_NULL_HANDLE;
        VkBuffer m_indexBuffer = VK_NULL_HANDLE;
        VkDeviceMemory m_indexBufferMemory = VK_NULL_HANDLE;

        std::vector<char> m_vertShaderCode;
        std::vector<char> m_fragShaderCode;

        std::vector<float> m_vertices;
        std::vector<uint32_t> m_indices;

        bool m_initialized = false;
        bool m_shadersLoaded = false;
        bool m_meshLoaded = false;
      };

      // Global renderer instance for exported functions
      inline RendererDemo *g_renderer = nullptr;
    }
  }
}

// Export the render function
//
// display, window => x11 window data to render on. provided externally
// vertices => attribute layout = [x, y, z, r, g, b]
// indices => which vertex indices to render (groups of 3 vertices)
// vertShadpath, fragShadPath => path to spv files to read from and create the shader modules

// using namespace PrimordialPower::Renderers::Obsidian;

// EXPORT_CLASS_New(Renderer, Renderer);
// EXPORT_CLASS_Dispose(Renderer, Renderer);

// EXPORT void Renderer_Init(Renderer *renderer, intptr_t display, uint64_t window);
// EXPORT void Renderer_LoadShaders(Renderer *renderer, const char *vertShadPath, const char *fragShadPath);
// EXPORT void Renderer_LoadMesh(Renderer *renderer, float *vertices, uint32_t vertexCount, uint32_t *indices, uint32_t indexCount);
// EXPORT void Renderer_Render(Renderer *renderer);
