#include "VulkanRenderer.hpp"

#ifdef _WIN32
#define VK_USE_PLATFORM_WIN32_KHR
#define NOMINMAX
#include <windows.h>
#endif

#include <vulkan/vulkan.h>
#ifdef _WIN32
#include <vulkan/vulkan_win32.h>
#endif

#include <vector>
#include <iostream>
#include <fstream>
#include <cstring>
#include <algorithm>
#include <mutex>

// Global Vulkan state
static VkInstance instance = VK_NULL_HANDLE;
static VkPhysicalDevice physicalDevice = VK_NULL_HANDLE;
static VkDevice device = VK_NULL_HANDLE;
static VkQueue graphicsQueue = VK_NULL_HANDLE;
static VkSurfaceKHR surface = VK_NULL_HANDLE;
static VkSwapchainKHR swapchain = VK_NULL_HANDLE;
static std::vector<VkImage> swapchainImages;
static std::vector<VkImageView> swapchainImageViews;
static VkRenderPass renderPass = VK_NULL_HANDLE;
static std::vector<VkFramebuffer> framebuffers;
static VkPipelineLayout pipelineLayout = VK_NULL_HANDLE;
static VkPipeline graphicsPipeline = VK_NULL_HANDLE;
static VkCommandPool commandPool = VK_NULL_HANDLE;
static VkCommandBuffer commandBuffer = VK_NULL_HANDLE;
static VkSemaphore imageAvailableSemaphore = VK_NULL_HANDLE;
static VkSemaphore renderFinishedSemaphore = VK_NULL_HANDLE;
static VkFence inFlightFence = VK_NULL_HANDLE;

static uint32_t graphicsQueueFamily = UINT32_MAX;
static VkFormat swapchainFormat;
static VkExtent2D swapchainExtent;
static bool framebufferResized = false;
static std::mutex resizeMutex;

// Helper function to read shader files
static std::vector<char> ReadShaderFile(const char *filename)
{
  std::ifstream file(filename, std::ios::ate | std::ios::binary);
  if (!file.is_open())
  {
    std::cerr << "Failed to open shader file: " << filename << std::endl;
    return {};
  }

  size_t fileSize = (size_t)file.tellg();
  std::vector<char> buffer(fileSize);
  file.seekg(0);
  file.read(buffer.data(), fileSize);
  file.close();

  return buffer;
}

// Helper to create shader module
static VkShaderModule CreateShaderModule(const std::vector<char> &code)
{
  VkShaderModuleCreateInfo createInfo{};
  createInfo.sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
  createInfo.codeSize = code.size();
  createInfo.pCode = reinterpret_cast<const uint32_t *>(code.data());

  VkShaderModule shaderModule;
  if (vkCreateShaderModule(device, &createInfo, nullptr, &shaderModule) != VK_SUCCESS)
  {
    std::cerr << "Failed to create shader module" << std::endl;
    return VK_NULL_HANDLE;
  }

  return shaderModule;
}

// Helper function to recreate swapchain
static bool RecreateSwapchain(uint32_t width, uint32_t height)
{
  // Handle minimization - don't recreate with 0 size
  if (width == 0 || height == 0)
  {
    return true; // Not an error, just wait
  }

  vkDeviceWaitIdle(device);

  // Clean up old swapchain resources
  for (auto framebuffer : framebuffers)
  {
    vkDestroyFramebuffer(device, framebuffer, nullptr);
  }
  framebuffers.clear();

  for (auto imageView : swapchainImageViews)
  {
    vkDestroyImageView(device, imageView, nullptr);
  }
  swapchainImageViews.clear();

  // Store old swapchain to pass to creation
  VkSwapchainKHR oldSwapchain = swapchain;

  // Get updated surface capabilities
  VkSurfaceCapabilitiesKHR capabilities;
  vkGetPhysicalDeviceSurfaceCapabilitiesKHR(physicalDevice, surface, &capabilities);

  // Clamp dimensions to supported range
  VkExtent2D newExtent;
  newExtent.width = width;
  newExtent.height = height;

  if (capabilities.currentExtent.width != UINT32_MAX)
  {
    newExtent = capabilities.currentExtent;
  }
  else
  {
    newExtent.width = std::max(capabilities.minImageExtent.width,
                                std::min(capabilities.maxImageExtent.width, width));
    newExtent.height = std::max(capabilities.minImageExtent.height,
                                 std::min(capabilities.maxImageExtent.height, height));
  }

  // Update the global swapchainExtent with proper locking
  {
    std::lock_guard<std::mutex> lock(resizeMutex);
    swapchainExtent = newExtent;
  }

  // Create new swapchain
  VkSwapchainCreateInfoKHR swapchainCreateInfo{};
  swapchainCreateInfo.sType = VK_STRUCTURE_TYPE_SWAPCHAIN_CREATE_INFO_KHR;
  swapchainCreateInfo.surface = surface;
  swapchainCreateInfo.minImageCount = capabilities.minImageCount + 1;
  if (capabilities.maxImageCount > 0 && swapchainCreateInfo.minImageCount > capabilities.maxImageCount)
  {
    swapchainCreateInfo.minImageCount = capabilities.maxImageCount;
  }
  swapchainCreateInfo.imageFormat = swapchainFormat;
  swapchainCreateInfo.imageColorSpace = VK_COLOR_SPACE_SRGB_NONLINEAR_KHR;
  swapchainCreateInfo.imageExtent = newExtent;
  swapchainCreateInfo.imageArrayLayers = 1;
  swapchainCreateInfo.imageUsage = VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT;
  swapchainCreateInfo.imageSharingMode = VK_SHARING_MODE_EXCLUSIVE;
  swapchainCreateInfo.preTransform = capabilities.currentTransform;
  swapchainCreateInfo.compositeAlpha = VK_COMPOSITE_ALPHA_OPAQUE_BIT_KHR;
  swapchainCreateInfo.presentMode = VK_PRESENT_MODE_FIFO_KHR;
  swapchainCreateInfo.clipped = VK_TRUE;
  swapchainCreateInfo.oldSwapchain = oldSwapchain;

  VkSwapchainKHR newSwapchain;
  if (vkCreateSwapchainKHR(device, &swapchainCreateInfo, nullptr, &newSwapchain) != VK_SUCCESS)
  {
    std::cerr << "Failed to recreate swapchain" << std::endl;
    return false;
  }

  // Destroy old swapchain
  vkDestroySwapchainKHR(device, oldSwapchain, nullptr);
  swapchain = newSwapchain;

  // Get new swapchain images
  uint32_t imageCount;
  vkGetSwapchainImagesKHR(device, swapchain, &imageCount, nullptr);
  swapchainImages.resize(imageCount);
  vkGetSwapchainImagesKHR(device, swapchain, &imageCount, swapchainImages.data());

  // Recreate image views
  swapchainImageViews.resize(imageCount);
  for (size_t i = 0; i < imageCount; i++)
  {
    VkImageViewCreateInfo viewCreateInfo{};
    viewCreateInfo.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
    viewCreateInfo.image = swapchainImages[i];
    viewCreateInfo.viewType = VK_IMAGE_VIEW_TYPE_2D;
    viewCreateInfo.format = swapchainFormat;
    viewCreateInfo.components.r = VK_COMPONENT_SWIZZLE_IDENTITY;
    viewCreateInfo.components.g = VK_COMPONENT_SWIZZLE_IDENTITY;
    viewCreateInfo.components.b = VK_COMPONENT_SWIZZLE_IDENTITY;
    viewCreateInfo.components.a = VK_COMPONENT_SWIZZLE_IDENTITY;
    viewCreateInfo.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
    viewCreateInfo.subresourceRange.baseMipLevel = 0;
    viewCreateInfo.subresourceRange.levelCount = 1;
    viewCreateInfo.subresourceRange.baseArrayLayer = 0;
    viewCreateInfo.subresourceRange.layerCount = 1;

    if (vkCreateImageView(device, &viewCreateInfo, nullptr, &swapchainImageViews[i]) != VK_SUCCESS)
    {
      std::cerr << "Failed to recreate image view" << std::endl;
      return false;
    }
  }

  // Recreate framebuffers
  framebuffers.resize(swapchainImageViews.size());
  for (size_t i = 0; i < swapchainImageViews.size(); i++)
  {
    VkImageView attachments[] = {swapchainImageViews[i]};

    VkFramebufferCreateInfo framebufferInfo{};
    framebufferInfo.sType = VK_STRUCTURE_TYPE_FRAMEBUFFER_CREATE_INFO;
    framebufferInfo.renderPass = renderPass;
    framebufferInfo.attachmentCount = 1;
    framebufferInfo.pAttachments = attachments;
    framebufferInfo.width = newExtent.width;
    framebufferInfo.height = newExtent.height;
    framebufferInfo.layers = 1;

    if (vkCreateFramebuffer(device, &framebufferInfo, nullptr, &framebuffers[i]) != VK_SUCCESS)
    {
      std::cerr << "Failed to recreate framebuffer" << std::endl;
      return false;
    }
  }

  std::cout << "Swapchain recreated: " << newExtent.width << "x" << newExtent.height << std::endl;
  return true;
}

EXPORT bool InitVulkan(ViewportData *viewportData)
{
  if (!viewportData)
  {
    std::cerr << "ViewportData is null" << std::endl;
    return false;
  }

  // 1. Create Vulkan Instance
  VkApplicationInfo appInfo{};
  appInfo.sType = VK_STRUCTURE_TYPE_APPLICATION_INFO;
  appInfo.pApplicationName = "ApplicationName";
  appInfo.applicationVersion = VK_MAKE_VERSION(1, 0, 0);
  appInfo.pEngineName = "PrimordialPower";
  appInfo.engineVersion = VK_MAKE_VERSION(1, 0, 0);
  appInfo.apiVersion = VK_API_VERSION_1_2;

  std::vector<const char *> extensions = {
#ifdef _WIN32
      VK_KHR_WIN32_SURFACE_EXTENSION_NAME,
#endif
      VK_KHR_SURFACE_EXTENSION_NAME};

  VkInstanceCreateInfo instanceCreateInfo{};
  instanceCreateInfo.sType = VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO;
  instanceCreateInfo.pApplicationInfo = &appInfo;
  instanceCreateInfo.enabledExtensionCount = static_cast<uint32_t>(extensions.size());
  instanceCreateInfo.ppEnabledExtensionNames = extensions.data();

  if (vkCreateInstance(&instanceCreateInfo, nullptr, &instance) != VK_SUCCESS)
  {
    std::cerr << "Failed to create Vulkan instance" << std::endl;
    return false;
  }

  // 2. Create Win32 Surface
#ifdef _WIN32
  VkWin32SurfaceCreateInfoKHR surfaceCreateInfo{};
  surfaceCreateInfo.sType = VK_STRUCTURE_TYPE_WIN32_SURFACE_CREATE_INFO_KHR;
  surfaceCreateInfo.hwnd = static_cast<HWND>(viewportData->Native.Win32.HWnd);
  surfaceCreateInfo.hinstance = static_cast<HINSTANCE>(viewportData->Native.Win32.HInstance);

  if (vkCreateWin32SurfaceKHR(instance, &surfaceCreateInfo, nullptr, &surface) != VK_SUCCESS)
  {
    std::cerr << "Failed to create Win32 surface" << std::endl;
    return false;
  }
#endif

  // 3. Select Physical Device
  uint32_t deviceCount = 0;
  vkEnumeratePhysicalDevices(instance, &deviceCount, nullptr);
  if (deviceCount == 0)
  {
    std::cerr << "No Vulkan-capable GPUs found" << std::endl;
    return false;
  }

  std::vector<VkPhysicalDevice> devices(deviceCount);
  vkEnumeratePhysicalDevices(instance, &deviceCount, devices.data());
  physicalDevice = devices[0]; // Pick first device

  // 4. Find Graphics Queue Family
  uint32_t queueFamilyCount = 0;
  vkGetPhysicalDeviceQueueFamilyProperties(physicalDevice, &queueFamilyCount, nullptr);
  std::vector<VkQueueFamilyProperties> queueFamilies(queueFamilyCount);
  vkGetPhysicalDeviceQueueFamilyProperties(physicalDevice, &queueFamilyCount, queueFamilies.data());

  for (uint32_t i = 0; i < queueFamilyCount; i++)
  {
    if (queueFamilies[i].queueFlags & VK_QUEUE_GRAPHICS_BIT)
    {
      VkBool32 presentSupport = false;
      vkGetPhysicalDeviceSurfaceSupportKHR(physicalDevice, i, surface, &presentSupport);
      if (presentSupport)
      {
        graphicsQueueFamily = i;
        break;
      }
    }
  }

  if (graphicsQueueFamily == UINT32_MAX)
  {
    std::cerr << "Failed to find suitable queue family" << std::endl;
    return false;
  }

  // 5. Create Logical Device
  float queuePriority = 1.0f;
  VkDeviceQueueCreateInfo queueCreateInfo{};
  queueCreateInfo.sType = VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO;
  queueCreateInfo.queueFamilyIndex = graphicsQueueFamily;
  queueCreateInfo.queueCount = 1;
  queueCreateInfo.pQueuePriorities = &queuePriority;

  const char *deviceExtensions[] = {VK_KHR_SWAPCHAIN_EXTENSION_NAME};

  VkDeviceCreateInfo deviceCreateInfo{};
  deviceCreateInfo.sType = VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO;
  deviceCreateInfo.queueCreateInfoCount = 1;
  deviceCreateInfo.pQueueCreateInfos = &queueCreateInfo;
  deviceCreateInfo.enabledExtensionCount = 1;
  deviceCreateInfo.ppEnabledExtensionNames = deviceExtensions;

  if (vkCreateDevice(physicalDevice, &deviceCreateInfo, nullptr, &device) != VK_SUCCESS)
  {
    std::cerr << "Failed to create logical device" << std::endl;
    return false;
  }

  vkGetDeviceQueue(device, graphicsQueueFamily, 0, &graphicsQueue);

  // 6. Create Swapchain
  VkSurfaceCapabilitiesKHR capabilities;
  vkGetPhysicalDeviceSurfaceCapabilitiesKHR(physicalDevice, surface, &capabilities);

  swapchainExtent.width = viewportData->Width;
  swapchainExtent.height = viewportData->Height;

  uint32_t formatCount;
  vkGetPhysicalDeviceSurfaceFormatsKHR(physicalDevice, surface, &formatCount, nullptr);
  std::vector<VkSurfaceFormatKHR> formats(formatCount);
  vkGetPhysicalDeviceSurfaceFormatsKHR(physicalDevice, surface, &formatCount, formats.data());

  VkSurfaceFormatKHR surfaceFormat = formats[0];
  for (const auto &format : formats)
  {
    if (format.format == VK_FORMAT_B8G8R8A8_UNORM && format.colorSpace == VK_COLOR_SPACE_SRGB_NONLINEAR_KHR)
    {
      surfaceFormat = format;
      break;
    }
  }
  swapchainFormat = surfaceFormat.format;

  VkSwapchainCreateInfoKHR swapchainCreateInfo{};
  swapchainCreateInfo.sType = VK_STRUCTURE_TYPE_SWAPCHAIN_CREATE_INFO_KHR;
  swapchainCreateInfo.surface = surface;
  swapchainCreateInfo.minImageCount = capabilities.minImageCount + 1;
  swapchainCreateInfo.imageFormat = swapchainFormat;
  swapchainCreateInfo.imageColorSpace = surfaceFormat.colorSpace;
  swapchainCreateInfo.imageExtent = swapchainExtent;
  swapchainCreateInfo.imageArrayLayers = 1;
  swapchainCreateInfo.imageUsage = VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT;
  swapchainCreateInfo.imageSharingMode = VK_SHARING_MODE_EXCLUSIVE;
  swapchainCreateInfo.preTransform = capabilities.currentTransform;
  swapchainCreateInfo.compositeAlpha = VK_COMPOSITE_ALPHA_OPAQUE_BIT_KHR;
  swapchainCreateInfo.presentMode = VK_PRESENT_MODE_FIFO_KHR;
  swapchainCreateInfo.clipped = VK_TRUE;

  if (vkCreateSwapchainKHR(device, &swapchainCreateInfo, nullptr, &swapchain) != VK_SUCCESS)
  {
    std::cerr << "Failed to create swapchain" << std::endl;
    return false;
  }

  // Get swapchain images
  uint32_t imageCount;
  vkGetSwapchainImagesKHR(device, swapchain, &imageCount, nullptr);
  swapchainImages.resize(imageCount);
  vkGetSwapchainImagesKHR(device, swapchain, &imageCount, swapchainImages.data());

  // Create image views
  swapchainImageViews.resize(imageCount);
  for (size_t i = 0; i < imageCount; i++)
  {
    VkImageViewCreateInfo viewCreateInfo{};
    viewCreateInfo.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
    viewCreateInfo.image = swapchainImages[i];
    viewCreateInfo.viewType = VK_IMAGE_VIEW_TYPE_2D;
    viewCreateInfo.format = swapchainFormat;
    viewCreateInfo.components.r = VK_COMPONENT_SWIZZLE_IDENTITY;
    viewCreateInfo.components.g = VK_COMPONENT_SWIZZLE_IDENTITY;
    viewCreateInfo.components.b = VK_COMPONENT_SWIZZLE_IDENTITY;
    viewCreateInfo.components.a = VK_COMPONENT_SWIZZLE_IDENTITY;
    viewCreateInfo.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
    viewCreateInfo.subresourceRange.baseMipLevel = 0;
    viewCreateInfo.subresourceRange.levelCount = 1;
    viewCreateInfo.subresourceRange.baseArrayLayer = 0;
    viewCreateInfo.subresourceRange.layerCount = 1;

    if (vkCreateImageView(device, &viewCreateInfo, nullptr, &swapchainImageViews[i]) != VK_SUCCESS)
    {
      std::cerr << "Failed to create image view" << std::endl;
      return false;
    }
  }

  // 7. Create Render Pass
  VkAttachmentDescription colorAttachment{};
  colorAttachment.format = swapchainFormat;
  colorAttachment.samples = VK_SAMPLE_COUNT_1_BIT;
  colorAttachment.loadOp = VK_ATTACHMENT_LOAD_OP_CLEAR;
  colorAttachment.storeOp = VK_ATTACHMENT_STORE_OP_STORE;
  colorAttachment.stencilLoadOp = VK_ATTACHMENT_LOAD_OP_DONT_CARE;
  colorAttachment.stencilStoreOp = VK_ATTACHMENT_STORE_OP_DONT_CARE;
  colorAttachment.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
  colorAttachment.finalLayout = VK_IMAGE_LAYOUT_PRESENT_SRC_KHR;

  VkAttachmentReference colorAttachmentRef{};
  colorAttachmentRef.attachment = 0;
  colorAttachmentRef.layout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;

  VkSubpassDescription subpass{};
  subpass.pipelineBindPoint = VK_PIPELINE_BIND_POINT_GRAPHICS;
  subpass.colorAttachmentCount = 1;
  subpass.pColorAttachments = &colorAttachmentRef;

  VkRenderPassCreateInfo renderPassInfo{};
  renderPassInfo.sType = VK_STRUCTURE_TYPE_RENDER_PASS_CREATE_INFO;
  renderPassInfo.attachmentCount = 1;
  renderPassInfo.pAttachments = &colorAttachment;
  renderPassInfo.subpassCount = 1;
  renderPassInfo.pSubpasses = &subpass;

  if (vkCreateRenderPass(device, &renderPassInfo, nullptr, &renderPass) != VK_SUCCESS)
  {
    std::cerr << "Failed to create render pass" << std::endl;
    return false;
  }

  // 8. Load Shaders and Create Pipeline
  auto vertShaderCode = ReadShaderFile("triangle_vert.spv");
  auto fragShaderCode = ReadShaderFile("triangle_frag.spv");

  if (vertShaderCode.empty() || fragShaderCode.empty())
  {
    std::cerr << "Failed to load shader files" << std::endl;
    return false;
  }

  VkShaderModule vertShaderModule = CreateShaderModule(vertShaderCode);
  VkShaderModule fragShaderModule = CreateShaderModule(fragShaderCode);

  VkPipelineShaderStageCreateInfo vertShaderStageInfo{};
  vertShaderStageInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
  vertShaderStageInfo.stage = VK_SHADER_STAGE_VERTEX_BIT;
  vertShaderStageInfo.module = vertShaderModule;
  vertShaderStageInfo.pName = "main";

  VkPipelineShaderStageCreateInfo fragShaderStageInfo{};
  fragShaderStageInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
  fragShaderStageInfo.stage = VK_SHADER_STAGE_FRAGMENT_BIT;
  fragShaderStageInfo.module = fragShaderModule;
  fragShaderStageInfo.pName = "main";

  VkPipelineShaderStageCreateInfo shaderStages[] = {vertShaderStageInfo, fragShaderStageInfo};

  VkPipelineVertexInputStateCreateInfo vertexInputInfo{};
  vertexInputInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_VERTEX_INPUT_STATE_CREATE_INFO;
  vertexInputInfo.vertexBindingDescriptionCount = 0;
  vertexInputInfo.vertexAttributeDescriptionCount = 0;

  VkPipelineInputAssemblyStateCreateInfo inputAssembly{};
  inputAssembly.sType = VK_STRUCTURE_TYPE_PIPELINE_INPUT_ASSEMBLY_STATE_CREATE_INFO;
  inputAssembly.topology = VK_PRIMITIVE_TOPOLOGY_TRIANGLE_LIST;
  inputAssembly.primitiveRestartEnable = VK_FALSE;

  VkViewport viewport{};
  viewport.x = 0.0f;
  viewport.y = 0.0f;
  viewport.width = static_cast<float>(swapchainExtent.width);
  viewport.height = static_cast<float>(swapchainExtent.height);
  viewport.minDepth = 0.0f;
  viewport.maxDepth = 1.0f;

  VkRect2D scissor{};
  scissor.offset = {0, 0};
  scissor.extent = swapchainExtent;

  VkPipelineViewportStateCreateInfo viewportState{};
  viewportState.sType = VK_STRUCTURE_TYPE_PIPELINE_VIEWPORT_STATE_CREATE_INFO;
  viewportState.viewportCount = 1;
  viewportState.pViewports = &viewport;
  viewportState.scissorCount = 1;
  viewportState.pScissors = &scissor;

  VkPipelineRasterizationStateCreateInfo rasterizer{};
  rasterizer.sType = VK_STRUCTURE_TYPE_PIPELINE_RASTERIZATION_STATE_CREATE_INFO;
  rasterizer.depthClampEnable = VK_FALSE;
  rasterizer.rasterizerDiscardEnable = VK_FALSE;
  rasterizer.polygonMode = VK_POLYGON_MODE_FILL;
  rasterizer.lineWidth = 1.0f;
  rasterizer.cullMode = VK_CULL_MODE_BACK_BIT;
  rasterizer.frontFace = VK_FRONT_FACE_CLOCKWISE;
  rasterizer.depthBiasEnable = VK_FALSE;

  VkPipelineMultisampleStateCreateInfo multisampling{};
  multisampling.sType = VK_STRUCTURE_TYPE_PIPELINE_MULTISAMPLE_STATE_CREATE_INFO;
  multisampling.sampleShadingEnable = VK_FALSE;
  multisampling.rasterizationSamples = VK_SAMPLE_COUNT_1_BIT;

  VkPipelineColorBlendAttachmentState colorBlendAttachment{};
  colorBlendAttachment.colorWriteMask = VK_COLOR_COMPONENT_R_BIT | VK_COLOR_COMPONENT_G_BIT | VK_COLOR_COMPONENT_B_BIT | VK_COLOR_COMPONENT_A_BIT;
  colorBlendAttachment.blendEnable = VK_FALSE;

  VkPipelineColorBlendStateCreateInfo colorBlending{};
  colorBlending.sType = VK_STRUCTURE_TYPE_PIPELINE_COLOR_BLEND_STATE_CREATE_INFO;
  colorBlending.logicOpEnable = VK_FALSE;
  colorBlending.attachmentCount = 1;
  colorBlending.pAttachments = &colorBlendAttachment;

  VkPipelineLayoutCreateInfo pipelineLayoutInfo{};
  pipelineLayoutInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO;

  if (vkCreatePipelineLayout(device, &pipelineLayoutInfo, nullptr, &pipelineLayout) != VK_SUCCESS)
  {
    std::cerr << "Failed to create pipeline layout" << std::endl;
    return false;
  }

  VkGraphicsPipelineCreateInfo pipelineInfo{};
  pipelineInfo.sType = VK_STRUCTURE_TYPE_GRAPHICS_PIPELINE_CREATE_INFO;
  pipelineInfo.stageCount = 2;
  pipelineInfo.pStages = shaderStages;
  pipelineInfo.pVertexInputState = &vertexInputInfo;
  pipelineInfo.pInputAssemblyState = &inputAssembly;
  pipelineInfo.pViewportState = &viewportState;
  pipelineInfo.pRasterizationState = &rasterizer;
  pipelineInfo.pMultisampleState = &multisampling;
  pipelineInfo.pColorBlendState = &colorBlending;
  pipelineInfo.layout = pipelineLayout;
  pipelineInfo.renderPass = renderPass;
  pipelineInfo.subpass = 0;

  if (vkCreateGraphicsPipelines(device, VK_NULL_HANDLE, 1, &pipelineInfo, nullptr, &graphicsPipeline) != VK_SUCCESS)
  {
    std::cerr << "Failed to create graphics pipeline" << std::endl;
    return false;
  }

  vkDestroyShaderModule(device, vertShaderModule, nullptr);
  vkDestroyShaderModule(device, fragShaderModule, nullptr);

  // 9. Create Framebuffers
  framebuffers.resize(swapchainImageViews.size());
  for (size_t i = 0; i < swapchainImageViews.size(); i++)
  {
    VkImageView attachments[] = {swapchainImageViews[i]};

    VkFramebufferCreateInfo framebufferInfo{};
    framebufferInfo.sType = VK_STRUCTURE_TYPE_FRAMEBUFFER_CREATE_INFO;
    framebufferInfo.renderPass = renderPass;
    framebufferInfo.attachmentCount = 1;
    framebufferInfo.pAttachments = attachments;
    framebufferInfo.width = swapchainExtent.width;
    framebufferInfo.height = swapchainExtent.height;
    framebufferInfo.layers = 1;

    if (vkCreateFramebuffer(device, &framebufferInfo, nullptr, &framebuffers[i]) != VK_SUCCESS)
    {
      std::cerr << "Failed to create framebuffer" << std::endl;
      return false;
    }
  }

  // 10. Create Command Pool
  VkCommandPoolCreateInfo poolInfo{};
  poolInfo.sType = VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO;
  poolInfo.queueFamilyIndex = graphicsQueueFamily;
  poolInfo.flags = VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT;

  if (vkCreateCommandPool(device, &poolInfo, nullptr, &commandPool) != VK_SUCCESS)
  {
    std::cerr << "Failed to create command pool" << std::endl;
    return false;
  }

  // 11. Allocate Command Buffer
  VkCommandBufferAllocateInfo allocInfo{};
  allocInfo.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO;
  allocInfo.commandPool = commandPool;
  allocInfo.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY;
  allocInfo.commandBufferCount = 1;

  if (vkAllocateCommandBuffers(device, &allocInfo, &commandBuffer) != VK_SUCCESS)
  {
    std::cerr << "Failed to allocate command buffer" << std::endl;
    return false;
  }

  // 12. Create Synchronization Objects
  VkSemaphoreCreateInfo semaphoreInfo{};
  semaphoreInfo.sType = VK_STRUCTURE_TYPE_SEMAPHORE_CREATE_INFO;

  VkFenceCreateInfo fenceInfo{};
  fenceInfo.sType = VK_STRUCTURE_TYPE_FENCE_CREATE_INFO;
  fenceInfo.flags = VK_FENCE_CREATE_SIGNALED_BIT;

  if (vkCreateSemaphore(device, &semaphoreInfo, nullptr, &imageAvailableSemaphore) != VK_SUCCESS ||
      vkCreateSemaphore(device, &semaphoreInfo, nullptr, &renderFinishedSemaphore) != VK_SUCCESS ||
      vkCreateFence(device, &fenceInfo, nullptr, &inFlightFence) != VK_SUCCESS)
  {
    std::cerr << "Failed to create synchronization objects" << std::endl;
    return false;
  }

  std::cout << "Vulkan initialized successfully!" << std::endl;
  return true;
}

EXPORT void Render()
{
  // Don't render if window is minimized
  uint32_t currentWidth, currentHeight;
  {
    std::lock_guard<std::mutex> lock(resizeMutex);
    currentWidth = swapchainExtent.width;
    currentHeight = swapchainExtent.height;
  }

  if (currentWidth == 0 || currentHeight == 0)
  {
    return;
  }

  vkWaitForFences(device, 1, &inFlightFence, VK_TRUE, UINT64_MAX);

  uint32_t imageIndex;
  VkResult result = vkAcquireNextImageKHR(device, swapchain, UINT64_MAX, imageAvailableSemaphore, VK_NULL_HANDLE, &imageIndex);

  if (result == VK_ERROR_OUT_OF_DATE_KHR)
  {
    // Swapchain is out of date (e.g., window was resized), recreate it
    RecreateSwapchain(currentWidth, currentHeight);
    return;
  }
  else if (result != VK_SUCCESS && result != VK_SUBOPTIMAL_KHR)
  {
    std::cerr << "Failed to acquire swapchain image!" << std::endl;
    return;
  }

  vkResetFences(device, 1, &inFlightFence);

  vkResetCommandBuffer(commandBuffer, 0);

  // Record command buffer
  VkCommandBufferBeginInfo beginInfo{};
  beginInfo.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO;
  vkBeginCommandBuffer(commandBuffer, &beginInfo);

  VkRenderPassBeginInfo renderPassInfo{};
  renderPassInfo.sType = VK_STRUCTURE_TYPE_RENDER_PASS_BEGIN_INFO;
  renderPassInfo.renderPass = renderPass;
  renderPassInfo.framebuffer = framebuffers[imageIndex];
  renderPassInfo.renderArea.offset = {0, 0};
  renderPassInfo.renderArea.extent = swapchainExtent;

  VkClearValue clearColor = {{{0.0f, 0.0f, 0.0f, 1.0f}}};
  renderPassInfo.clearValueCount = 1;
  renderPassInfo.pClearValues = &clearColor;

  vkCmdBeginRenderPass(commandBuffer, &renderPassInfo, VK_SUBPASS_CONTENTS_INLINE);
  vkCmdBindPipeline(commandBuffer, VK_PIPELINE_BIND_POINT_GRAPHICS, graphicsPipeline);
  vkCmdDraw(commandBuffer, 3, 1, 0, 0);
  vkCmdEndRenderPass(commandBuffer);

  vkEndCommandBuffer(commandBuffer);

  // Submit command buffer
  VkSubmitInfo submitInfo{};
  submitInfo.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO;

  VkSemaphore waitSemaphores[] = {imageAvailableSemaphore};
  VkPipelineStageFlags waitStages[] = {VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT};
  submitInfo.waitSemaphoreCount = 1;
  submitInfo.pWaitSemaphores = waitSemaphores;
  submitInfo.pWaitDstStageMask = waitStages;
  submitInfo.commandBufferCount = 1;
  submitInfo.pCommandBuffers = &commandBuffer;

  VkSemaphore signalSemaphores[] = {renderFinishedSemaphore};
  submitInfo.signalSemaphoreCount = 1;
  submitInfo.pSignalSemaphores = signalSemaphores;

  vkQueueSubmit(graphicsQueue, 1, &submitInfo, inFlightFence);

  // Present
  VkPresentInfoKHR presentInfo{};
  presentInfo.sType = VK_STRUCTURE_TYPE_PRESENT_INFO_KHR;
  presentInfo.waitSemaphoreCount = 1;
  presentInfo.pWaitSemaphores = signalSemaphores;

  VkSwapchainKHR swapchains[] = {swapchain};
  presentInfo.swapchainCount = 1;
  presentInfo.pSwapchains = swapchains;
  presentInfo.pImageIndices = &imageIndex;

  result = vkQueuePresentKHR(graphicsQueue, &presentInfo);

  bool needsRecreate = false;
  {
    std::lock_guard<std::mutex> lock(resizeMutex);
    if (result == VK_ERROR_OUT_OF_DATE_KHR || result == VK_SUBOPTIMAL_KHR || framebufferResized)
    {
      needsRecreate = true;
      framebufferResized = false;
      currentWidth = swapchainExtent.width;
      currentHeight = swapchainExtent.height;
    }
  }

  if (needsRecreate)
  {
    RecreateSwapchain(currentWidth, currentHeight);
  }
  else if (result != VK_SUCCESS)
  {
    std::cerr << "Failed to present swapchain image!" << std::endl;
  }
}

EXPORT void Cleanup()
{
  vkDeviceWaitIdle(device);

  vkDestroySemaphore(device, imageAvailableSemaphore, nullptr);
  vkDestroySemaphore(device, renderFinishedSemaphore, nullptr);
  vkDestroyFence(device, inFlightFence, nullptr);
  vkDestroyCommandPool(device, commandPool, nullptr);

  for (auto framebuffer : framebuffers)
    vkDestroyFramebuffer(device, framebuffer, nullptr);

  vkDestroyPipeline(device, graphicsPipeline, nullptr);
  vkDestroyPipelineLayout(device, pipelineLayout, nullptr);
  vkDestroyRenderPass(device, renderPass, nullptr);

  for (auto imageView : swapchainImageViews)
    vkDestroyImageView(device, imageView, nullptr);

  vkDestroySwapchainKHR(device, swapchain, nullptr);
  vkDestroyDevice(device, nullptr);
  vkDestroySurfaceKHR(instance, surface, nullptr);
  vkDestroyInstance(instance, nullptr);

  std::cout << "Vulkan cleaned up" << std::endl;
}

EXPORT void ResizeViewport(uint32_t width, uint32_t height)
{
  std::lock_guard<std::mutex> lock(resizeMutex);
  swapchainExtent.width = width;
  swapchainExtent.height = height;
  framebufferResized = true;
}
