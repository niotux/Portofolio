#include "Renderer.hpp"

#include "lib/All.h"

#ifdef OS_LINUX
#define SURFACE_EXTENSION_NAME VK_KHR_XLIB_SURFACE_EXTENSION_NAME
#endif

#ifdef OS_WINDOWS
#define SURFACE_EXTENSION_NAME VK_KHR_WIN32_SURFACE_EXTENSION_NAME
#endif

#ifdef OS_MACOS
#define SURFACE_EXTENSION_NAME VK_KHR_METAL_SURFACE_EXTENSION_NAME
#endif

using namespace PrimordialPower::Renderers::Obsidian;

Renderer::Renderer()
{
}
//
//
//
//
//
//
Renderer::~Renderer()
{
  // Destroy all cached device bundles
  for (auto *bundle : _deviceBundles)
  {
    if (bundle->Device != VK_NULL_HANDLE)
    {
      vkDestroyDevice(bundle->Device, nullptr);
    }
    delete bundle;
  }
  _deviceBundles.clear();

  if (_instance != VK_NULL_HANDLE)
  {
    vkDestroyInstance(_instance, nullptr);
    _instance = VK_NULL_HANDLE;
  }
}
//
//
//
//
//
//
// Init only creates the VkInstance.
// Device creation is deferred until the first context is created,
// because we need a VkSurfaceKHR to properly detect queue families.
//
void Renderer::Init(const Data::ApplicationData &app)
{
  _app = app;
  VulkanCreateInstance(app);
}
//
//
//
//
//
//
// Creates a fully initialized RendererContext for a viewport.
// This is the main orchestrator - it creates the surface first,
// then lazily creates or reuses a device, then builds the
// entire rendering pipeline for this viewport.
//
void Renderer::CreateContext(RendererContext *context_out, const Data::ViewportData &viewport)
{
  if (context_out == nullptr)
  {
    throw std::runtime_error("Invalid nullptr argument!");
  }

  Data::ContextData ctx{};
  ctx.Instance = _instance;

  // Step 1: Create surface (needs only VkInstance + window handle)
  ctx.Surface = VulkanCreateSurface(viewport);

  // Step 2: Find or create a device that can present to this surface
  ctx.Device = FindOrCreateDevice(ctx.Surface);

  // Steps 3-9: Build the rendering pipeline
  CreateSwapchain(ctx, viewport);
  CreateImageViews(ctx);
  CreateRenderPass(ctx);
  CreateFramebuffers(ctx);
  CreateCommandPool(ctx);
  CreateCommandBuffer(ctx);
  CreateSyncObjects(ctx);

  // Construct the context in-place
  new (context_out) RendererContext(ctx);
}
//
//
//
//
//
//
void Renderer::VulkanCreateInstance(const Data::ApplicationData &app)
{
  VkApplicationInfo appInfo{};
  appInfo.sType = VK_STRUCTURE_TYPE_APPLICATION_INFO;
  appInfo.pApplicationName = app.AppName;
  appInfo.applicationVersion = app.AppVersion;
  appInfo.pEngineName = app.EngineName;
  appInfo.engineVersion = app.EngineVersion;
  appInfo.apiVersion = app.VkApiVersion;

  VkInstanceCreateInfo createInfo{};
  createInfo.sType = VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO;
  createInfo.pApplicationInfo = &appInfo;

  const char *extensions[] = {
      VK_KHR_SURFACE_EXTENSION_NAME,
      SURFACE_EXTENSION_NAME,
  };
  createInfo.enabledExtensionCount = 2;
  createInfo.ppEnabledExtensionNames = extensions;
  createInfo.enabledLayerCount = 0;

  if (vkCreateInstance(&createInfo, nullptr, &_instance) != VK_SUCCESS)
  {
    throw std::runtime_error("Failed to create Vulkan instance");
  }
}
//
//
//
//
//
//
VkSurfaceKHR Renderer::VulkanCreateSurface(const Data::ViewportData &viewport)
{
  VkSurfaceKHR surface = VK_NULL_HANDLE;
  VkResult err;

#ifdef OS_LINUX
  VkXlibSurfaceCreateInfoKHR surfaceCreateInfo{};
  surfaceCreateInfo.sType = VK_STRUCTURE_TYPE_XLIB_SURFACE_CREATE_INFO_KHR;
  surfaceCreateInfo.dpy = reinterpret_cast<Display *>(viewport.Handle.X11.Display);
  surfaceCreateInfo.window = static_cast<Window>(viewport.Handle.X11.Window);

  err = vkCreateXlibSurfaceKHR(_instance, &surfaceCreateInfo, nullptr, &surface);
#endif

#ifdef OS_WINDOWS
  VkWin32SurfaceCreateInfoKHR createInfo{};
  createInfo.sType = VK_STRUCTURE_TYPE_WIN32_SURFACE_CREATE_INFO_KHR;
  createInfo.hinstance = reinterpret_cast<HINSTANCE>(viewport.Handle.Win32.HInstance);
  createInfo.hwnd = reinterpret_cast<HWND>(viewport.Handle.Win32.HWnd);

  err = vkCreateWin32SurfaceKHR(_instance, &createInfo, nullptr, &surface);
#endif

#ifdef OS_MACOS
  err = VK_ERROR_FEATURE_NOT_PRESENT;
#endif

  if (err != VK_SUCCESS)
  {
    throw std::runtime_error("Failed to create window surface");
  }

  return surface;
}
//
//
//
//
//
//
// Searches cached DeviceBundles for one that can present to the given surface.
// If none found, creates a new one.
//
Data::DeviceBundle *Renderer::FindOrCreateDevice(VkSurfaceKHR surface)
{
  // Check cached devices first
  for (auto *bundle : _deviceBundles)
  {
    VkBool32 presentSupport = false;
    vkGetPhysicalDeviceSurfaceSupportKHR(
        bundle->PhysicalDevice,
        bundle->PresentQueueFamily,
        surface,
        &presentSupport);

    if (presentSupport)
    {
      return bundle; // Reuse existing device
    }
  }

  // No cached device works, create a new one
  return PickAndCreateDevice(surface);
}
//
//
//
//
//
//
// Enumerates physical devices, picks the best one that supports both
// graphics and presentation to the given surface, creates a logical
// device, and caches the result.
//
Data::DeviceBundle *Renderer::PickAndCreateDevice(VkSurfaceKHR surface)
{
  // Enumerate all GPUs
  uint32_t deviceCount = 0;
  vkEnumeratePhysicalDevices(_instance, &deviceCount, nullptr);
  if (deviceCount == 0)
  {
    throw std::runtime_error("Failed to find GPUs with Vulkan support");
  }

  std::vector<VkPhysicalDevice> devices(deviceCount);
  vkEnumeratePhysicalDevices(_instance, &deviceCount, devices.data());

  // Score and select the best device
  VkPhysicalDevice bestDevice = VK_NULL_HANDLE;
  uint32_t bestGraphicsFamily = 0;
  uint32_t bestPresentFamily = 0;
  uint32_t bestScore = 0;

  for (const auto &device : devices)
  {
    // Check API version support
    VkPhysicalDeviceProperties props{};
    vkGetPhysicalDeviceProperties(device, &props);
    if (props.apiVersion < _app.VkApiVersion)
    {
      continue;
    }

    // Score by device type
    uint32_t score = 0;
    switch (props.deviceType)
    {
    case VK_PHYSICAL_DEVICE_TYPE_CPU:
      score = 10;
      break;
    case VK_PHYSICAL_DEVICE_TYPE_VIRTUAL_GPU:
      score = 20;
      break;
    case VK_PHYSICAL_DEVICE_TYPE_INTEGRATED_GPU:
      score = 30;
      break;
    case VK_PHYSICAL_DEVICE_TYPE_DISCRETE_GPU:
      score = 40;
      break;
    default:
      score = 1;
      break;
    }

    // Find queue families
    uint32_t queueFamilyCount = 0;
    vkGetPhysicalDeviceQueueFamilyProperties(device, &queueFamilyCount, nullptr);
    std::vector<VkQueueFamilyProperties> queueFamilies(queueFamilyCount);
    vkGetPhysicalDeviceQueueFamilyProperties(device, &queueFamilyCount, queueFamilies.data());

    bool foundGraphics = false;
    bool foundPresent = false;
    uint32_t graphicsFamily = 0;
    uint32_t presentFamily = 0;

    for (uint32_t i = 0; i < queueFamilyCount; i++)
    {
      if (queueFamilies[i].queueFlags & VK_QUEUE_GRAPHICS_BIT)
      {
        graphicsFamily = i;
        foundGraphics = true;
      }

      VkBool32 presentSupport = false;
      vkGetPhysicalDeviceSurfaceSupportKHR(device, i, surface, &presentSupport);
      if (presentSupport)
      {
        presentFamily = i;
        foundPresent = true;
      }

      if (foundGraphics && foundPresent)
        break;
    }

    if (foundGraphics && foundPresent && score > bestScore)
    {
      bestScore = score;
      bestDevice = device;
      bestGraphicsFamily = graphicsFamily;
      bestPresentFamily = presentFamily;
    }
  }

  if (bestDevice == VK_NULL_HANDLE)
  {
    throw std::runtime_error("Failed to find a suitable GPU");
  }

  // Create logical device
  std::vector<VkDeviceQueueCreateInfo> queueCreateInfos;
  float queuePriority = 1.0f;

  VkDeviceQueueCreateInfo graphicsQueueCreateInfo{};
  graphicsQueueCreateInfo.sType = VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO;
  graphicsQueueCreateInfo.queueFamilyIndex = bestGraphicsFamily;
  graphicsQueueCreateInfo.queueCount = 1;
  graphicsQueueCreateInfo.pQueuePriorities = &queuePriority;
  queueCreateInfos.push_back(graphicsQueueCreateInfo);

  if (bestPresentFamily != bestGraphicsFamily)
  {
    VkDeviceQueueCreateInfo presentQueueCreateInfo{};
    presentQueueCreateInfo.sType = VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO;
    presentQueueCreateInfo.queueFamilyIndex = bestPresentFamily;
    presentQueueCreateInfo.queueCount = 1;
    presentQueueCreateInfo.pQueuePriorities = &queuePriority;
    queueCreateInfos.push_back(presentQueueCreateInfo);
  }

  VkPhysicalDeviceFeatures deviceFeatures{};

  VkDeviceCreateInfo createInfo{};
  createInfo.sType = VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO;
  createInfo.queueCreateInfoCount = static_cast<uint32_t>(queueCreateInfos.size());
  createInfo.pQueueCreateInfos = queueCreateInfos.data();
  createInfo.pEnabledFeatures = &deviceFeatures;

  const char *deviceExtensions[] = {VK_KHR_SWAPCHAIN_EXTENSION_NAME};
  createInfo.enabledExtensionCount = 1;
  createInfo.ppEnabledExtensionNames = deviceExtensions;
  createInfo.enabledLayerCount = 0;

  VkDevice device;
  if (vkCreateDevice(bestDevice, &createInfo, nullptr, &device) != VK_SUCCESS)
  {
    throw std::runtime_error("Failed to create logical device");
  }

  // Build the bundle
  auto *bundle = new Data::DeviceBundle{};
  bundle->PhysicalDevice = bestDevice;
  bundle->Device = device;
  bundle->GraphicsQueueFamily = bestGraphicsFamily;
  bundle->PresentQueueFamily = bestPresentFamily;

  vkGetDeviceQueue(device, bestGraphicsFamily, 0, &bundle->GraphicsQueue);
  vkGetDeviceQueue(device, bestPresentFamily, 0, &bundle->PresentQueue);

  _deviceBundles.push_back(bundle);
  return bundle;
}
//
//
//
//
//
//
void Renderer::CreateSwapchain(Data::ContextData &ctx, const Data::ViewportData &viewport)
{
  VkPhysicalDevice physicalDevice = ctx.Device->PhysicalDevice;
  VkDevice device = ctx.Device->Device;

  // Query what the surface supports
  VkSurfaceCapabilitiesKHR capabilities;
  vkGetPhysicalDeviceSurfaceCapabilitiesKHR(physicalDevice, ctx.Surface, &capabilities);

  // Get supported formats
  uint32_t formatCount;
  vkGetPhysicalDeviceSurfaceFormatsKHR(physicalDevice, ctx.Surface, &formatCount, nullptr);
  std::vector<VkSurfaceFormatKHR> formats(formatCount);
  vkGetPhysicalDeviceSurfaceFormatsKHR(physicalDevice, ctx.Surface, &formatCount, formats.data());

  // Get supported present modes
  uint32_t presentModeCount;
  vkGetPhysicalDeviceSurfacePresentModesKHR(physicalDevice, ctx.Surface, &presentModeCount, nullptr);
  std::vector<VkPresentModeKHR> presentModes(presentModeCount);
  vkGetPhysicalDeviceSurfacePresentModesKHR(physicalDevice, ctx.Surface, &presentModeCount, presentModes.data());

  // Choose surface format - prefer SRGB for correct color display
  VkSurfaceFormatKHR surfaceFormat = formats[0];
  for (const auto &format : formats)
  {
    if (format.format == VK_FORMAT_B8G8R8A8_SRGB && format.colorSpace == VK_COLOR_SPACE_SRGB_NONLINEAR_KHR)
    {
      surfaceFormat = format;
      break;
    }
  }
  ctx.Format = surfaceFormat.format;

  // Choose present mode - prefer mailbox (triple buffering) for smooth rendering
  VkPresentModeKHR presentMode = VK_PRESENT_MODE_FIFO_KHR;
  for (const auto &mode : presentModes)
  {
    if (mode == VK_PRESENT_MODE_MAILBOX_KHR)
    {
      presentMode = mode;
      break;
    }
  }

  // Choose swap extent (resolution)
  if (capabilities.currentExtent.width != std::numeric_limits<uint32_t>::max())
  {
    ctx.Extent = capabilities.currentExtent;
  }
  else
  {
    ctx.Extent = {viewport.Width, viewport.Height};
    ctx.Extent.width = std::clamp(ctx.Extent.width, capabilities.minImageExtent.width, capabilities.maxImageExtent.width);
    ctx.Extent.height = std::clamp(ctx.Extent.height, capabilities.minImageExtent.height, capabilities.maxImageExtent.height);
  }

  // Request one more image than minimum for triple buffering
  uint32_t imageCount = capabilities.minImageCount + 1;
  if (capabilities.maxImageCount > 0 && imageCount > capabilities.maxImageCount)
  {
    imageCount = capabilities.maxImageCount;
  }

  VkSwapchainCreateInfoKHR swapchainCreateInfo{};
  swapchainCreateInfo.sType = VK_STRUCTURE_TYPE_SWAPCHAIN_CREATE_INFO_KHR;
  swapchainCreateInfo.surface = ctx.Surface;
  swapchainCreateInfo.minImageCount = imageCount;
  swapchainCreateInfo.imageFormat = surfaceFormat.format;
  swapchainCreateInfo.imageColorSpace = surfaceFormat.colorSpace;
  swapchainCreateInfo.imageExtent = ctx.Extent;
  swapchainCreateInfo.imageArrayLayers = 1;
  swapchainCreateInfo.imageUsage = VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT;

  // Handle queue family sharing
  uint32_t queueFamilyIndices[] = {ctx.Device->GraphicsQueueFamily, ctx.Device->PresentQueueFamily};
  if (ctx.Device->GraphicsQueueFamily != ctx.Device->PresentQueueFamily)
  {
    swapchainCreateInfo.imageSharingMode = VK_SHARING_MODE_CONCURRENT;
    swapchainCreateInfo.queueFamilyIndexCount = 2;
    swapchainCreateInfo.pQueueFamilyIndices = queueFamilyIndices;
  }
  else
  {
    swapchainCreateInfo.imageSharingMode = VK_SHARING_MODE_EXCLUSIVE;
  }

  swapchainCreateInfo.preTransform = capabilities.currentTransform;
  swapchainCreateInfo.compositeAlpha = VK_COMPOSITE_ALPHA_OPAQUE_BIT_KHR;
  swapchainCreateInfo.presentMode = presentMode;
  swapchainCreateInfo.clipped = VK_TRUE;
  swapchainCreateInfo.oldSwapchain = VK_NULL_HANDLE;

  if (vkCreateSwapchainKHR(device, &swapchainCreateInfo, nullptr, &ctx.Swapchain) != VK_SUCCESS)
  {
    throw std::runtime_error("Failed to create swap chain");
  }

  // Get the swapchain images (owned by the swapchain, we don't create them)
  vkGetSwapchainImagesKHR(device, ctx.Swapchain, &imageCount, nullptr);
  ctx.Images.resize(imageCount);
  vkGetSwapchainImagesKHR(device, ctx.Swapchain, &imageCount, ctx.Images.data());
}
//
//
//
//
//
//
void Renderer::CreateImageViews(Data::ContextData &ctx)
{
  VkDevice device = ctx.Device->Device;

  ctx.ImageViews.resize(ctx.Images.size());

  for (size_t i = 0; i < ctx.Images.size(); i++)
  {
    VkImageViewCreateInfo createInfo{};
    createInfo.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
    createInfo.image = ctx.Images[i];
    createInfo.viewType = VK_IMAGE_VIEW_TYPE_2D;
    createInfo.format = ctx.Format;

    createInfo.components.r = VK_COMPONENT_SWIZZLE_IDENTITY;
    createInfo.components.g = VK_COMPONENT_SWIZZLE_IDENTITY;
    createInfo.components.b = VK_COMPONENT_SWIZZLE_IDENTITY;
    createInfo.components.a = VK_COMPONENT_SWIZZLE_IDENTITY;

    createInfo.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
    createInfo.subresourceRange.baseMipLevel = 0;
    createInfo.subresourceRange.levelCount = 1;
    createInfo.subresourceRange.baseArrayLayer = 0;
    createInfo.subresourceRange.layerCount = 1;

    if (vkCreateImageView(device, &createInfo, nullptr, &ctx.ImageViews[i]) != VK_SUCCESS)
    {
      throw std::runtime_error("Failed to create image views");
    }
  }
}
//
//
//
//
//
//
void Renderer::CreateRenderPass(Data::ContextData &ctx)
{
  VkDevice device = ctx.Device->Device;

  // Color attachment (the swapchain image we render to)
  VkAttachmentDescription colorAttachment{};
  colorAttachment.format = ctx.Format;
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

  // External operations must complete before we write colors
  VkSubpassDependency dependency{};
  dependency.srcSubpass = VK_SUBPASS_EXTERNAL;
  dependency.dstSubpass = 0;
  dependency.srcStageMask = VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT;
  dependency.srcAccessMask = 0;
  dependency.dstStageMask = VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT;
  dependency.dstAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;

  VkRenderPassCreateInfo renderPassInfo{};
  renderPassInfo.sType = VK_STRUCTURE_TYPE_RENDER_PASS_CREATE_INFO;
  renderPassInfo.attachmentCount = 1;
  renderPassInfo.pAttachments = &colorAttachment;
  renderPassInfo.subpassCount = 1;
  renderPassInfo.pSubpasses = &subpass;
  renderPassInfo.dependencyCount = 1;
  renderPassInfo.pDependencies = &dependency;

  if (vkCreateRenderPass(device, &renderPassInfo, nullptr, &ctx.RenderPass) != VK_SUCCESS)
  {
    throw std::runtime_error("Failed to create render pass");
  }
}
//
//
//
//
//
//
void Renderer::CreateFramebuffers(Data::ContextData &ctx)
{
  VkDevice device = ctx.Device->Device;

  ctx.Framebuffers.resize(ctx.ImageViews.size());

  for (size_t i = 0; i < ctx.ImageViews.size(); i++)
  {
    VkImageView attachments[] = {ctx.ImageViews[i]};

    VkFramebufferCreateInfo framebufferInfo{};
    framebufferInfo.sType = VK_STRUCTURE_TYPE_FRAMEBUFFER_CREATE_INFO;
    framebufferInfo.renderPass = ctx.RenderPass;
    framebufferInfo.attachmentCount = 1;
    framebufferInfo.pAttachments = attachments;
    framebufferInfo.width = ctx.Extent.width;
    framebufferInfo.height = ctx.Extent.height;
    framebufferInfo.layers = 1;

    if (vkCreateFramebuffer(device, &framebufferInfo, nullptr, &ctx.Framebuffers[i]) != VK_SUCCESS)
    {
      throw std::runtime_error("Failed to create framebuffer");
    }
  }
}
//
//
//
//
//
//
void Renderer::CreateCommandPool(Data::ContextData &ctx)
{
  VkDevice device = ctx.Device->Device;

  VkCommandPoolCreateInfo poolInfo{};
  poolInfo.sType = VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO;
  poolInfo.flags = VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT;
  poolInfo.queueFamilyIndex = ctx.Device->GraphicsQueueFamily;

  if (vkCreateCommandPool(device, &poolInfo, nullptr, &ctx.CommandPool) != VK_SUCCESS)
  {
    throw std::runtime_error("Failed to create command pool");
  }
}
//
//
//
//
//
//
void Renderer::CreateCommandBuffer(Data::ContextData &ctx)
{
  VkDevice device = ctx.Device->Device;

  VkCommandBufferAllocateInfo allocInfo{};
  allocInfo.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO;
  allocInfo.commandPool = ctx.CommandPool;
  allocInfo.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY;
  allocInfo.commandBufferCount = 1;

  if (vkAllocateCommandBuffers(device, &allocInfo, &ctx.CommandBuffer) != VK_SUCCESS)
  {
    throw std::runtime_error("Failed to allocate command buffers");
  }
}
//
//
//
//
//
//
void Renderer::CreateSyncObjects(Data::ContextData &ctx)
{
  VkDevice device = ctx.Device->Device;

  VkSemaphoreCreateInfo semaphoreInfo{};
  semaphoreInfo.sType = VK_STRUCTURE_TYPE_SEMAPHORE_CREATE_INFO;

  VkFenceCreateInfo fenceInfo{};
  fenceInfo.sType = VK_STRUCTURE_TYPE_FENCE_CREATE_INFO;
  fenceInfo.flags = VK_FENCE_CREATE_SIGNALED_BIT; // Start signaled so first frame doesn't wait forever

  if (vkCreateSemaphore(device, &semaphoreInfo, nullptr, &ctx.ImageAvailableSemaphore) != VK_SUCCESS ||
      vkCreateSemaphore(device, &semaphoreInfo, nullptr, &ctx.RenderFinishedSemaphore) != VK_SUCCESS ||
      vkCreateFence(device, &fenceInfo, nullptr, &ctx.InFlightFence) != VK_SUCCESS)
  {
    throw std::runtime_error("Failed to create synchronization objects");
  }
}
//
//
//
//
//
//
// ============================================================================
// C EXPORTS - For calling from C# via P/Invoke
// ============================================================================

using RendererT = PrimordialPower::Renderers::Obsidian::Renderer;
using RendererContextT = PrimordialPower::Renderers::Obsidian::RendererContext;

EXPORT_CLASS_New(RendererT, Renderer);
EXPORT_CLASS_Dispose(RendererT, Renderer);

EXPORT void Renderer_Init(RendererT *renderer, uint32_t appVersion, uint32_t engineVersion, uint32_t vkApiVersion, const char *appName, const char *engineName)
{
  Data::ApplicationData app{};
  app.AppVersion = appVersion;
  app.EngineVersion = engineVersion;
  app.VkApiVersion = vkApiVersion;
  app.AppName = appName;
  app.EngineName = engineName;
  renderer->Init(app);
}

EXPORT RendererContextT *Renderer_CreateContext(RendererT *renderer, Data::ViewportData &viewport)
{
  auto *ctx = static_cast<RendererContextT *>(::operator new(sizeof(RendererContextT)));
  renderer->CreateContext(ctx, viewport);
  return ctx;
}

EXPORT void RendererContext_DrawFrame(RendererContextT *context)
{
  context->DrawFrame();
}

EXPORT void RendererContext_RecreateSwapchain(RendererContextT *context, uint32_t width, uint32_t height)
{
  context->RecreateSwapchain(width, height);
}

EXPORT void RendererContext_Dispose(RendererContextT *context)
{
  context->~RendererContext();
  ::operator delete(context);
}
