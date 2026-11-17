#include "demo-Renderer.hpp"

#include <fstream>
#include <stdexcept>
#include <cstring>
#include <algorithm>
#include <limits>

namespace PrimordialPower::Renderers::Obsidian
{

  // ============================================================================
  // CONSTRUCTOR / DESTRUCTOR
  // ============================================================================

  RendererDemo::RendererDemo()
  {
  }

  RendererDemo::~RendererDemo()
  {
    Cleanup();
  }

  // ============================================================================
  // PUBLIC API - These are the main entry points you call from outside
  // ============================================================================
  //
  // VULKAN INITIALIZATION ORDER (must be done in this sequence):
  //
  //   1. Instance        - Connection to Vulkan library
  //   2. Surface         - Bridge between Vulkan and your window system (X11)
  //   3. Physical Device - Pick which GPU to use
  //   4. Logical Device  - Interface to the GPU with queues for submitting work
  //   5. Swapchain       - Collection of images to render to and present
  //   6. Image Views     - How to access swapchain images
  //   7. Render Pass     - Describes what attachments (color, depth) are used
  //   8. Command Pool    - Allocator for command buffers
  //   9. Command Buffer  - Records GPU commands
  //  10. Sync Objects    - Semaphores and fences for CPU/GPU synchronization
  //
  // After initialization, you need:
  //   - Shaders (vertex + fragment) compiled to SPIR-V
  //   - Mesh data (vertices + indices)
  //   - Graphics Pipeline (combines shaders, vertex layout, render pass, etc.)
  //   - Framebuffers (connect image views to render pass)
  //
  // ============================================================================

  void RendererDemo::Init(intptr_t display, uint64_t window)
  {
    CreateInstance();
    CreateSurface(display, window);
    PickPhysicalDevice();
    CreateLogicalDevice();
    CreateSwapchain();
    CreateImageViews();
    CreateRenderPass();
    CreateCommandPool();
    CreateCommandBuffer();
    CreateSyncObjects();
    m_initialized = true;
  }

  void RendererDemo::LoadShaders(const char *vertShadPath, const char *fragShadPath)
  {
    // Read vertex shader
    std::ifstream vertFile(vertShadPath, std::ios::ate | std::ios::binary);
    if (!vertFile.is_open())
    {
      throw std::runtime_error("Failed to open vertex shader file");
    }
    size_t vertSize = (size_t)vertFile.tellg();
    m_vertShaderCode.resize(vertSize);
    vertFile.seekg(0);
    vertFile.read(m_vertShaderCode.data(), vertSize);
    vertFile.close();

    // Read fragment shader
    std::ifstream fragFile(fragShadPath, std::ios::ate | std::ios::binary);
    if (!fragFile.is_open())
    {
      throw std::runtime_error("Failed to open fragment shader file");
    }
    size_t fragSize = (size_t)fragFile.tellg();
    m_fragShaderCode.resize(fragSize);
    fragFile.seekg(0);
    fragFile.read(m_fragShaderCode.data(), fragSize);
    fragFile.close();

    m_shadersLoaded = true;

    // If we already have mesh data, create the pipeline
    if (m_initialized && m_meshLoaded)
    {
      CreateGraphicsPipeline();
      CreateFramebuffers();
    }
  }

  void RendererDemo::LoadMesh(float *vertices, uint32_t vertexCount, uint32_t *indices, uint32_t indexCount)
  {
    m_vertices.assign(vertices, vertices + vertexCount);
    m_indices.assign(indices, indices + indexCount);
    m_meshLoaded = true;

    if (m_initialized)
    {
      CreateVertexBuffer();
      CreateIndexBuffer();

      // If shaders are loaded, create the pipeline
      if (m_shadersLoaded)
      {
        CreateGraphicsPipeline();
        CreateFramebuffers();
      }
    }
  }

  // ============================================================================
  // RENDER LOOP - Called every frame
  // ============================================================================
  //
  // THE FRAME RENDERING SEQUENCE:
  //
  //   1. Wait for previous frame to finish (fence)
  //   2. Acquire next swapchain image (which image to render to)
  //   3. Record commands into command buffer (what to draw)
  //   4. Submit command buffer to graphics queue
  //   5. Present the rendered image to the screen
  //
  // SYNCHRONIZATION EXPLAINED:
  //
  //   - Fence: CPU waits for GPU. Used to ensure previous frame is done
  //            before we start recording new commands.
  //
  //   - Semaphore: GPU-to-GPU sync. Two semaphores here:
  //       * imageAvailable: Signals when swapchain image is ready to render to
  //       * renderFinished: Signals when rendering is done, safe to present
  //
  //   The flow is:
  //     [Acquire Image] --imageAvailable--> [Render] --renderFinished--> [Present]
  //
  // ============================================================================

  void RendererDemo::Render()
  {
    if (!m_initialized || !m_shadersLoaded || !m_meshLoaded)
    {
      return;
    }

    // STEP 1: Wait for previous frame
    // The fence ensures we don't overwrite a command buffer that's still in use
    vkWaitForFences(m_device, 1, &m_inFlightFence, VK_TRUE, UINT64_MAX);
    vkResetFences(m_device, 1, &m_inFlightFence);

    // STEP 2: Acquire next image from swapchain
    // imageAvailableSemaphore will be signaled when the image is ready
    uint32_t imageIndex;
    vkAcquireNextImageKHR(m_device, m_swapchain, UINT64_MAX, m_imageAvailableSemaphore, VK_NULL_HANDLE, &imageIndex);

    // STEP 3: Record draw commands
    vkResetCommandBuffer(m_commandBuffer, 0);
    RecordCommandBuffer(m_commandBuffer, imageIndex);

    // STEP 4: Submit to GPU
    VkSubmitInfo submitInfo{};
    submitInfo.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO;

    // Wait for image to be available before writing colors to it
    VkSemaphore waitSemaphores[] = {m_imageAvailableSemaphore};
    VkPipelineStageFlags waitStages[] = {VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT};
    submitInfo.waitSemaphoreCount = 1;
    submitInfo.pWaitSemaphores = waitSemaphores;
    submitInfo.pWaitDstStageMask = waitStages;
    submitInfo.commandBufferCount = 1;
    submitInfo.pCommandBuffers = &m_commandBuffer;

    // Signal renderFinished when done
    VkSemaphore signalSemaphores[] = {m_renderFinishedSemaphore};
    submitInfo.signalSemaphoreCount = 1;
    submitInfo.pSignalSemaphores = signalSemaphores;

    // Submit and signal the fence when complete (for CPU sync)
    if (vkQueueSubmit(m_graphicsQueue, 1, &submitInfo, m_inFlightFence) != VK_SUCCESS)
    {
      throw std::runtime_error("Failed to submit draw command buffer");
    }

    // STEP 5: Present to screen
    // Wait for rendering to finish before presenting
    VkPresentInfoKHR presentInfo{};
    presentInfo.sType = VK_STRUCTURE_TYPE_PRESENT_INFO_KHR;
    presentInfo.waitSemaphoreCount = 1;
    presentInfo.pWaitSemaphores = signalSemaphores;

    VkSwapchainKHR swapchains[] = {m_swapchain};
    presentInfo.swapchainCount = 1;
    presentInfo.pSwapchains = swapchains;
    presentInfo.pImageIndices = &imageIndex;

    vkQueuePresentKHR(m_presentQueue, &presentInfo);
  }

  // ============================================================================
  // SECTION 1: INSTANCE CREATION
  // ============================================================================
  //
  // The VkInstance is your connection to the Vulkan library.
  // Think of it as "starting up" Vulkan.
  //
  // Key concepts:
  //   - Extensions: Optional features. We need surface extensions to render
  //                 to a window. VK_KHR_SURFACE is the generic surface,
  //                 VK_KHR_XLIB_SURFACE is specifically for X11 windows.
  //
  //   - Layers: Debug/validation overlays. Not enabled here, but useful
  //             during development (e.g., VK_LAYER_KHRONOS_validation).
  //
  //   - API Version: Which Vulkan version to use. 1.0 is most compatible.
  //
  // ============================================================================

  void RendererDemo::CreateInstance()
  {
    // Metadata about your application (optional but good practice)
    VkApplicationInfo appInfo{};
    appInfo.sType = VK_STRUCTURE_TYPE_APPLICATION_INFO;
    appInfo.pApplicationName = "Obsidian RendererDemo";
    appInfo.applicationVersion = VK_MAKE_VERSION(1, 0, 0);
    appInfo.pEngineName = "PrimordialPower";
    appInfo.engineVersion = VK_MAKE_VERSION(1, 0, 0);
    appInfo.apiVersion = VK_API_VERSION_1_0;

    VkInstanceCreateInfo createInfo{};
    createInfo.sType = VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO;
    createInfo.pApplicationInfo = &appInfo;

    // Extensions needed to create a window surface
    const char *extensions[] = {
        VK_KHR_SURFACE_EXTENSION_NAME,     // Generic surface support
        VK_KHR_XLIB_SURFACE_EXTENSION_NAME // X11-specific surface
    };
    createInfo.enabledExtensionCount = 2;
    createInfo.ppEnabledExtensionNames = extensions;
    createInfo.enabledLayerCount = 0; // No validation layers (add for debugging)

    if (vkCreateInstance(&createInfo, nullptr, &m_instance) != VK_SUCCESS)
    {
      throw std::runtime_error("Failed to create Vulkan instance");
    }
  }

  // ============================================================================
  // SECTION 2: SURFACE CREATION
  // ============================================================================
  //
  // The VkSurfaceKHR is the bridge between Vulkan and your windowing system.
  // It represents the drawable area of your window.
  //
  // This is platform-specific:
  //   - X11 (Linux): VkXlibSurfaceCreateInfoKHR with Display* and Window
  //   - Wayland: VkWaylandSurfaceCreateInfoKHR
  //   - Windows: VkWin32SurfaceCreateInfoKHR with HWND
  //
  // The surface is used later to:
  //   - Check which GPUs can present to it
  //   - Create the swapchain (the images we render to)
  //
  // ============================================================================

  void RendererDemo::CreateSurface(intptr_t display, uint64_t window)
  {
    VkXlibSurfaceCreateInfoKHR createInfo{};
    createInfo.sType = VK_STRUCTURE_TYPE_XLIB_SURFACE_CREATE_INFO_KHR;
    createInfo.dpy = reinterpret_cast<Display *>(display); // X11 display connection
    createInfo.window = static_cast<Window>(window);       // X11 window handle

    if (vkCreateXlibSurfaceKHR(m_instance, &createInfo, nullptr, &m_surface) != VK_SUCCESS)
    {
      throw std::runtime_error("Failed to create window surface");
    }
  }

  // ============================================================================
  // SECTION 3: PHYSICAL DEVICE SELECTION
  // ============================================================================
  //
  // A VkPhysicalDevice represents an actual GPU in your system.
  // You might have multiple (integrated + discrete GPU).
  //
  // QUEUE FAMILIES:
  //   GPUs organize their capabilities into "queue families". Each family
  //   can do certain types of work:
  //
  //   - Graphics: Drawing triangles, running shaders
  //   - Compute: General-purpose GPU computing
  //   - Transfer: Copying data between buffers/images
  //   - Present: Displaying images to a surface
  //
  //   We need at least:
  //   1. A queue that supports GRAPHICS (to render)
  //   2. A queue that can PRESENT to our surface (to display)
  //
  //   Often these are the same queue family, but not always!
  //
  // ============================================================================

  void RendererDemo::PickPhysicalDevice()
  {
    // Enumerate all GPUs
    uint32_t deviceCount = 0;
    vkEnumeratePhysicalDevices(m_instance, &deviceCount, nullptr);
    if (deviceCount == 0)
    {
      throw std::runtime_error("Failed to find GPUs with Vulkan support");
    }

    std::vector<VkPhysicalDevice> devices(deviceCount);
    vkEnumeratePhysicalDevices(m_instance, &deviceCount, devices.data());

    // Find a GPU that supports what we need
    for (const auto &device : devices)
    {
      // Get queue families for this device
      uint32_t queueFamilyCount = 0;
      vkGetPhysicalDeviceQueueFamilyProperties(device, &queueFamilyCount, nullptr);
      std::vector<VkQueueFamilyProperties> queueFamilies(queueFamilyCount);
      vkGetPhysicalDeviceQueueFamilyProperties(device, &queueFamilyCount, queueFamilies.data());

      bool foundGraphics = false;
      bool foundPresent = false;

      for (uint32_t i = 0; i < queueFamilyCount; i++)
      {
        // Check for graphics support
        if (queueFamilies[i].queueFlags & VK_QUEUE_GRAPHICS_BIT)
        {
          m_graphicsQueueFamily = i;
          foundGraphics = true;
        }

        // Check for present support (can this queue present to our surface?)
        VkBool32 presentSupport = false;
        vkGetPhysicalDeviceSurfaceSupportKHR(device, i, m_surface, &presentSupport);
        if (presentSupport)
        {
          m_presentQueueFamily = i;
          foundPresent = true;
        }

        if (foundGraphics && foundPresent)
          break;
      }

      if (foundGraphics && foundPresent)
      {
        m_physicalDevice = device;
        return;
      }
    }

    throw std::runtime_error("Failed to find a suitable GPU");
  }

  // ============================================================================
  // SECTION 4: LOGICAL DEVICE CREATION
  // ============================================================================
  //
  // The VkDevice is your interface to the GPU. While VkPhysicalDevice
  // represents the actual hardware, VkDevice is your "session" with it.
  //
  // When creating a logical device, you specify:
  //   - Which queues you want (from which families)
  //   - Which device extensions to enable (we need swapchain support)
  //   - Which features to enable (none here, but could enable things like
  //     geometry shaders, tessellation, etc.)
  //
  // After creation, you retrieve handles to the actual queues you requested.
  //
  // ============================================================================

  void RendererDemo::CreateLogicalDevice()
  {
    std::vector<VkDeviceQueueCreateInfo> queueCreateInfos;
    float queuePriority = 1.0f; // Priority 0.0-1.0, affects scheduling

    // Request a graphics queue
    VkDeviceQueueCreateInfo graphicsQueueCreateInfo{};
    graphicsQueueCreateInfo.sType = VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO;
    graphicsQueueCreateInfo.queueFamilyIndex = m_graphicsQueueFamily;
    graphicsQueueCreateInfo.queueCount = 1;
    graphicsQueueCreateInfo.pQueuePriorities = &queuePriority;
    queueCreateInfos.push_back(graphicsQueueCreateInfo);

    // If present queue is different, request that too
    if (m_presentQueueFamily != m_graphicsQueueFamily)
    {
      VkDeviceQueueCreateInfo presentQueueCreateInfo{};
      presentQueueCreateInfo.sType = VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO;
      presentQueueCreateInfo.queueFamilyIndex = m_presentQueueFamily;
      presentQueueCreateInfo.queueCount = 1;
      presentQueueCreateInfo.pQueuePriorities = &queuePriority;
      queueCreateInfos.push_back(presentQueueCreateInfo);
    }

    // Device features we want (none for basic rendering)
    VkPhysicalDeviceFeatures deviceFeatures{};

    VkDeviceCreateInfo createInfo{};
    createInfo.sType = VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO;
    createInfo.queueCreateInfoCount = static_cast<uint32_t>(queueCreateInfos.size());
    createInfo.pQueueCreateInfos = queueCreateInfos.data();
    createInfo.pEnabledFeatures = &deviceFeatures;

    // We need swapchain support to present images
    const char *deviceExtensions[] = {VK_KHR_SWAPCHAIN_EXTENSION_NAME};
    createInfo.enabledExtensionCount = 1;
    createInfo.ppEnabledExtensionNames = deviceExtensions;
    createInfo.enabledLayerCount = 0;

    if (vkCreateDevice(m_physicalDevice, &createInfo, nullptr, &m_device) != VK_SUCCESS)
    {
      throw std::runtime_error("Failed to create logical device");
    }

    // Get handles to the queues we requested
    vkGetDeviceQueue(m_device, m_graphicsQueueFamily, 0, &m_graphicsQueue);
    vkGetDeviceQueue(m_device, m_presentQueueFamily, 0, &m_presentQueue);
  }

  // ============================================================================
  // SECTION 5: SWAPCHAIN CREATION
  // ============================================================================
  //
  // The swapchain is a queue of images waiting to be displayed.
  // When you render, you draw to one image while another is being shown.
  //
  // KEY SWAPCHAIN CONCEPTS:
  //
  //   - Image Count: How many images in the chain (2 = double buffering,
  //                  3 = triple buffering). More = smoother but more memory.
  //
  //   - Surface Format: Pixel format (e.g., BGRA 8-bit) and color space
  //                     (e.g., sRGB for correct color display).
  //
  //   - Present Mode: How images are presented:
  //       * FIFO: VSync on. Waits for vertical blank. No tearing.
  //       * MAILBOX: Triple buffering. Replaces queued images. Low latency.
  //       * IMMEDIATE: No sync. May tear but lowest latency.
  //
  //   - Extent: Resolution of swapchain images (usually = window size).
  //
  //   - Sharing Mode: If graphics and present queues are different,
  //                   images must be shared between them (CONCURRENT).
  //
  // ============================================================================

  void RendererDemo::CreateSwapchain()
  {
    // Query what the surface supports
    VkSurfaceCapabilitiesKHR capabilities;
    vkGetPhysicalDeviceSurfaceCapabilitiesKHR(m_physicalDevice, m_surface, &capabilities);

    // Get supported formats
    uint32_t formatCount;
    vkGetPhysicalDeviceSurfaceFormatsKHR(m_physicalDevice, m_surface, &formatCount, nullptr);
    std::vector<VkSurfaceFormatKHR> formats(formatCount);
    vkGetPhysicalDeviceSurfaceFormatsKHR(m_physicalDevice, m_surface, &formatCount, formats.data());

    // Get supported present modes
    uint32_t presentModeCount;
    vkGetPhysicalDeviceSurfacePresentModesKHR(m_physicalDevice, m_surface, &presentModeCount, nullptr);
    std::vector<VkPresentModeKHR> presentModes(presentModeCount);
    vkGetPhysicalDeviceSurfacePresentModesKHR(m_physicalDevice, m_surface, &presentModeCount, presentModes.data());

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
    m_swapchainImageFormat = surfaceFormat.format;

    // Choose present mode - prefer mailbox (triple buffering) for smooth rendering
    VkPresentModeKHR presentMode = VK_PRESENT_MODE_FIFO_KHR; // FIFO is always supported
    for (const auto &mode : presentModes)
    {
      if (mode == VK_PRESENT_MODE_MAILBOX_KHR)
      {
        presentMode = mode;
        break;
      }
    }

    // Choose swap extent (resolution)
    // If currentExtent is max uint32, the surface size is determined by the extent we specify
    if (capabilities.currentExtent.width != std::numeric_limits<uint32_t>::max())
    {
      m_swapchainExtent = capabilities.currentExtent;
    }
    else
    {
      m_swapchainExtent = {800, 600};
      m_swapchainExtent.width = std::clamp(m_swapchainExtent.width, capabilities.minImageExtent.width, capabilities.maxImageExtent.width);
      m_swapchainExtent.height = std::clamp(m_swapchainExtent.height, capabilities.minImageExtent.height, capabilities.maxImageExtent.height);
    }

    // Request one more image than minimum for triple buffering
    uint32_t imageCount = capabilities.minImageCount + 1;
    if (capabilities.maxImageCount > 0 && imageCount > capabilities.maxImageCount)
    {
      imageCount = capabilities.maxImageCount;
    }

    VkSwapchainCreateInfoKHR createInfo{};
    createInfo.sType = VK_STRUCTURE_TYPE_SWAPCHAIN_CREATE_INFO_KHR;
    createInfo.surface = m_surface;
    createInfo.minImageCount = imageCount;
    createInfo.imageFormat = surfaceFormat.format;
    createInfo.imageColorSpace = surfaceFormat.colorSpace;
    createInfo.imageExtent = m_swapchainExtent;
    createInfo.imageArrayLayers = 1;                             // Always 1 unless stereoscopic 3D
    createInfo.imageUsage = VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT; // We render directly to these

    // Handle queue family sharing
    uint32_t queueFamilyIndices[] = {m_graphicsQueueFamily, m_presentQueueFamily};
    if (m_graphicsQueueFamily != m_presentQueueFamily)
    {
      createInfo.imageSharingMode = VK_SHARING_MODE_CONCURRENT; // Shared between queues
      createInfo.queueFamilyIndexCount = 2;
      createInfo.pQueueFamilyIndices = queueFamilyIndices;
    }
    else
    {
      createInfo.imageSharingMode = VK_SHARING_MODE_EXCLUSIVE; // No sharing needed
    }

    createInfo.preTransform = capabilities.currentTransform;       // No rotation/flip
    createInfo.compositeAlpha = VK_COMPOSITE_ALPHA_OPAQUE_BIT_KHR; // Ignore alpha for compositing
    createInfo.presentMode = presentMode;
    createInfo.clipped = VK_TRUE;             // Don't render pixels covered by other windows
    createInfo.oldSwapchain = VK_NULL_HANDLE; // Used when recreating swapchain (resize)

    if (vkCreateSwapchainKHR(m_device, &createInfo, nullptr, &m_swapchain) != VK_SUCCESS)
    {
      throw std::runtime_error("Failed to create swap chain");
    }

    // Get the swapchain images (we don't create these, the swapchain owns them)
    vkGetSwapchainImagesKHR(m_device, m_swapchain, &imageCount, nullptr);
    m_swapchainImages.resize(imageCount);
    vkGetSwapchainImagesKHR(m_device, m_swapchain, &imageCount, m_swapchainImages.data());
  }

  // ============================================================================
  // SECTION 6: IMAGE VIEWS
  // ============================================================================
  //
  // VkImageView describes how to access a VkImage.
  //
  // Think of VkImage as raw image data and VkImageView as a "lens" to view it:
  //   - What type? (2D, 3D, cube map, array)
  //   - What format? (how to interpret the bytes)
  //   - Which parts? (mip levels, array layers)
  //   - Swizzle? (remap color channels)
  //
  // For swapchain images, we create simple 2D views.
  //
  // ============================================================================

  void RendererDemo::CreateImageViews()
  {
    m_swapchainImageViews.resize(m_swapchainImages.size());

    for (size_t i = 0; i < m_swapchainImages.size(); i++)
    {
      VkImageViewCreateInfo createInfo{};
      createInfo.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
      createInfo.image = m_swapchainImages[i];
      createInfo.viewType = VK_IMAGE_VIEW_TYPE_2D;
      createInfo.format = m_swapchainImageFormat;

      // No channel swizzling (RGBA stays RGBA)
      createInfo.components.r = VK_COMPONENT_SWIZZLE_IDENTITY;
      createInfo.components.g = VK_COMPONENT_SWIZZLE_IDENTITY;
      createInfo.components.b = VK_COMPONENT_SWIZZLE_IDENTITY;
      createInfo.components.a = VK_COMPONENT_SWIZZLE_IDENTITY;

      // Which part of the image to access
      createInfo.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT; // Color data
      createInfo.subresourceRange.baseMipLevel = 0;                       // First mip level
      createInfo.subresourceRange.levelCount = 1;                         // Only one mip level
      createInfo.subresourceRange.baseArrayLayer = 0;                     // First array layer
      createInfo.subresourceRange.layerCount = 1;                         // Only one layer

      if (vkCreateImageView(m_device, &createInfo, nullptr, &m_swapchainImageViews[i]) != VK_SUCCESS)
      {
        throw std::runtime_error("Failed to create image views");
      }
    }
  }

  // ============================================================================
  // SECTION 7: RENDER PASS
  // ============================================================================
  //
  // A render pass describes the structure of rendering operations:
  //   - What attachments (images) are used (color, depth, stencil)
  //   - How they're loaded/stored at pass start/end
  //   - How subpasses use and transition them
  //
  // ATTACHMENTS:
  //   Describe images used during rendering:
  //   - loadOp: What to do at start (CLEAR = fill with color, LOAD = keep)
  //   - storeOp: What to do at end (STORE = save, DONT_CARE = discard)
  //   - initialLayout: Expected image layout at start
  //   - finalLayout: Layout to transition to at end
  //
  // SUBPASSES:
  //   Stages within a render pass. We have just one that outputs color.
  //   Complex RendererDemos might have multiple (e.g., G-buffer pass, lighting pass).
  //
  // DEPENDENCIES:
  //   Define execution/memory barriers between subpasses.
  //   VK_SUBPASS_EXTERNAL means "operations outside this render pass".
  //
  // ============================================================================

  void RendererDemo::CreateRenderPass()
  {
    // Describe our color attachment (the swapchain image we render to)
    VkAttachmentDescription colorAttachment{};
    colorAttachment.format = m_swapchainImageFormat;
    colorAttachment.samples = VK_SAMPLE_COUNT_1_BIT;                   // No multisampling
    colorAttachment.loadOp = VK_ATTACHMENT_LOAD_OP_CLEAR;              // Clear to black at start
    colorAttachment.storeOp = VK_ATTACHMENT_STORE_OP_STORE;            // Keep the result
    colorAttachment.stencilLoadOp = VK_ATTACHMENT_LOAD_OP_DONT_CARE;   // No stencil
    colorAttachment.stencilStoreOp = VK_ATTACHMENT_STORE_OP_DONT_CARE; // No stencil
    colorAttachment.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;         // Don't care about previous contents
    colorAttachment.finalLayout = VK_IMAGE_LAYOUT_PRESENT_SRC_KHR;     // Ready for presentation

    // Reference to the attachment for use in subpass
    VkAttachmentReference colorAttachmentRef{};
    colorAttachmentRef.attachment = 0;                                    // Index into attachment array
    colorAttachmentRef.layout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL; // Optimal for writing colors

    // Our single subpass
    VkSubpassDescription subpass{};
    subpass.pipelineBindPoint = VK_PIPELINE_BIND_POINT_GRAPHICS; // This is a graphics subpass
    subpass.colorAttachmentCount = 1;
    subpass.pColorAttachments = &colorAttachmentRef;
    // pDepthStencilAttachment would go here for depth testing
    // pInputAttachments for reading from previous subpass outputs

    // Dependency: external operations must complete before we write colors
    VkSubpassDependency dependency{};
    dependency.srcSubpass = VK_SUBPASS_EXTERNAL; // Operations before render pass
    dependency.dstSubpass = 0;                   // Our subpass
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

    if (vkCreateRenderPass(m_device, &renderPassInfo, nullptr, &m_renderPass) != VK_SUCCESS)
    {
      throw std::runtime_error("Failed to create render pass");
    }
  }

  // ============================================================================
  // SECTION 8: GRAPHICS PIPELINE
  // ============================================================================
  //
  // The graphics pipeline is the heart of rendering. It defines EVERYTHING
  // about how vertices become pixels. In Vulkan, pipelines are immutable
  // after creation - you create different pipelines for different effects.
  //
  // PIPELINE STAGES (in order):
  //
  //   1. Input Assembler: Reads vertex/index buffers, assembles primitives
  //   2. Vertex Shader: Transforms each vertex (position, normals, etc.)
  //   3. Tessellation: Subdivides geometry (optional)
  //   4. Geometry Shader: Generates/modifies primitives (optional)
  //   5. Rasterization: Converts triangles to fragments (pixels)
  //   6. Fragment Shader: Computes color for each fragment
  //   7. Color Blending: Combines fragment color with framebuffer
  //
  // FIXED-FUNCTION STAGES (configured, not programmed):
  //   - Vertex Input: Describes vertex data layout
  //   - Input Assembly: How to group vertices (triangles, lines, etc.)
  //   - Viewport/Scissor: Transform and clip region
  //   - Rasterizer: Fill mode, culling, depth bias
  //   - Multisampling: Anti-aliasing
  //   - Color Blending: Alpha blending, logic ops
  //
  // PIPELINE LAYOUT:
  //   Describes resources the shaders can access (uniforms, textures).
  //   Empty here, but would include descriptor set layouts for UBOs, samplers.
  //
  // ============================================================================

  void RendererDemo::CreateGraphicsPipeline()
  {
    // Clean up old pipeline if recreating
    if (m_graphicsPipeline != VK_NULL_HANDLE)
    {
      vkDestroyPipeline(m_device, m_graphicsPipeline, nullptr);
      m_graphicsPipeline = VK_NULL_HANDLE;
    }
    if (m_pipelineLayout != VK_NULL_HANDLE)
    {
      vkDestroyPipelineLayout(m_device, m_pipelineLayout, nullptr);
      m_pipelineLayout = VK_NULL_HANDLE;
    }

    // ---- SHADER STAGES ----
    // Wrap SPIR-V bytecode in shader modules
    VkShaderModule vertShaderModule = CreateShaderModule(m_vertShaderCode);
    VkShaderModule fragShaderModule = CreateShaderModule(m_fragShaderCode);

    VkPipelineShaderStageCreateInfo vertShaderStageInfo{};
    vertShaderStageInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
    vertShaderStageInfo.stage = VK_SHADER_STAGE_VERTEX_BIT;
    vertShaderStageInfo.module = vertShaderModule;
    vertShaderStageInfo.pName = "main"; // Entry point function name

    VkPipelineShaderStageCreateInfo fragShaderStageInfo{};
    fragShaderStageInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
    fragShaderStageInfo.stage = VK_SHADER_STAGE_FRAGMENT_BIT;
    fragShaderStageInfo.module = fragShaderModule;
    fragShaderStageInfo.pName = "main";

    VkPipelineShaderStageCreateInfo shaderStages[] = {vertShaderStageInfo, fragShaderStageInfo};

    // ---- VERTEX INPUT ----
    // Describes the format of vertex data
    // Binding: which buffer slot (we use one buffer at binding 0)
    // Stride: bytes between vertices (6 floats = 24 bytes)
    VkVertexInputBindingDescription bindingDescription{};
    bindingDescription.binding = 0;
    bindingDescription.stride = 6 * sizeof(float);              // x,y,z,r,g,b
    bindingDescription.inputRate = VK_VERTEX_INPUT_RATE_VERTEX; // Per-vertex (not per-instance)

    // Attributes: individual vertex components
    // Location matches "layout(location = N)" in shader
    VkVertexInputAttributeDescription attributeDescriptions[2]{};

    // Position attribute at location 0
    attributeDescriptions[0].binding = 0;
    attributeDescriptions[0].location = 0;
    attributeDescriptions[0].format = VK_FORMAT_R32G32B32_SFLOAT; // vec3
    attributeDescriptions[0].offset = 0;

    // Color attribute at location 1
    attributeDescriptions[1].binding = 0;
    attributeDescriptions[1].location = 1;
    attributeDescriptions[1].format = VK_FORMAT_R32G32B32_SFLOAT; // vec3
    attributeDescriptions[1].offset = 3 * sizeof(float);          // After position

    VkPipelineVertexInputStateCreateInfo vertexInputInfo{};
    vertexInputInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_VERTEX_INPUT_STATE_CREATE_INFO;
    vertexInputInfo.vertexBindingDescriptionCount = 1;
    vertexInputInfo.pVertexBindingDescriptions = &bindingDescription;
    vertexInputInfo.vertexAttributeDescriptionCount = 2;
    vertexInputInfo.pVertexAttributeDescriptions = attributeDescriptions;

    // ---- INPUT ASSEMBLY ----
    // How to interpret vertices
    VkPipelineInputAssemblyStateCreateInfo inputAssembly{};
    inputAssembly.sType = VK_STRUCTURE_TYPE_PIPELINE_INPUT_ASSEMBLY_STATE_CREATE_INFO;
    inputAssembly.topology = VK_PRIMITIVE_TOPOLOGY_TRIANGLE_LIST; // Every 3 vertices = 1 triangle
    inputAssembly.primitiveRestartEnable = VK_FALSE;

    // ---- VIEWPORT & SCISSOR ----
    // Viewport: transforms normalized coordinates to framebuffer pixels
    // Scissor: clips pixels outside the rectangle
    VkViewport viewport{};
    viewport.x = 0.0f;
    viewport.y = 0.0f;
    viewport.width = (float)m_swapchainExtent.width;
    viewport.height = (float)m_swapchainExtent.height;
    viewport.minDepth = 0.0f;
    viewport.maxDepth = 1.0f;

    VkRect2D scissor{};
    scissor.offset = {0, 0};
    scissor.extent = m_swapchainExtent;

    VkPipelineViewportStateCreateInfo viewportState{};
    viewportState.sType = VK_STRUCTURE_TYPE_PIPELINE_VIEWPORT_STATE_CREATE_INFO;
    viewportState.viewportCount = 1;
    viewportState.pViewports = &viewport;
    viewportState.scissorCount = 1;
    viewportState.pScissors = &scissor;

    // ---- RASTERIZER ----
    // Converts triangles to fragments
    VkPipelineRasterizationStateCreateInfo rasterizer{};
    rasterizer.sType = VK_STRUCTURE_TYPE_PIPELINE_RASTERIZATION_STATE_CREATE_INFO;
    rasterizer.depthClampEnable = VK_FALSE;        // Clamp instead of discard fragments beyond near/far
    rasterizer.rasterizerDiscardEnable = VK_FALSE; // If true, geometry never passes through rasterizer
    rasterizer.polygonMode = VK_POLYGON_MODE_FILL; // Fill triangles (LINE = wireframe)
    rasterizer.lineWidth = 1.0f;
    rasterizer.cullMode = VK_CULL_MODE_BACK_BIT;    // Don't render back faces
    rasterizer.frontFace = VK_FRONT_FACE_CLOCKWISE; // Which winding order is "front"
    rasterizer.depthBiasEnable = VK_FALSE;          // Used for shadow mapping

    // ---- MULTISAMPLING ----
    // Anti-aliasing (disabled here)
    VkPipelineMultisampleStateCreateInfo multisampling{};
    multisampling.sType = VK_STRUCTURE_TYPE_PIPELINE_MULTISAMPLE_STATE_CREATE_INFO;
    multisampling.sampleShadingEnable = VK_FALSE;
    multisampling.rasterizationSamples = VK_SAMPLE_COUNT_1_BIT;

    // ---- COLOR BLENDING ----
    // How to combine fragment color with existing framebuffer color
    // Per-attachment settings
    VkPipelineColorBlendAttachmentState colorBlendAttachment{};
    colorBlendAttachment.colorWriteMask = VK_COLOR_COMPONENT_R_BIT | VK_COLOR_COMPONENT_G_BIT |
                                          VK_COLOR_COMPONENT_B_BIT | VK_COLOR_COMPONENT_A_BIT;
    colorBlendAttachment.blendEnable = VK_FALSE; // No blending, just overwrite
    // If blendEnable = VK_TRUE, you'd configure blend factors here for transparency

    VkPipelineColorBlendStateCreateInfo colorBlending{};
    colorBlending.sType = VK_STRUCTURE_TYPE_PIPELINE_COLOR_BLEND_STATE_CREATE_INFO;
    colorBlending.logicOpEnable = VK_FALSE; // Bitwise operations instead of blending
    colorBlending.attachmentCount = 1;
    colorBlending.pAttachments = &colorBlendAttachment;

    // ---- PIPELINE LAYOUT ----
    // Describes shader resource bindings (uniforms, textures)
    // Empty for now - no uniforms
    VkPipelineLayoutCreateInfo pipelineLayoutInfo{};
    pipelineLayoutInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO;
    // pSetLayouts would point to descriptor set layouts for UBOs, samplers, etc.
    // pPushConstantRanges for small, frequently-changing data

    if (vkCreatePipelineLayout(m_device, &pipelineLayoutInfo, nullptr, &m_pipelineLayout) != VK_SUCCESS)
    {
      throw std::runtime_error("Failed to create pipeline layout");
    }

    // ---- CREATE THE PIPELINE ----
    VkGraphicsPipelineCreateInfo pipelineInfo{};
    pipelineInfo.sType = VK_STRUCTURE_TYPE_GRAPHICS_PIPELINE_CREATE_INFO;
    pipelineInfo.stageCount = 2;
    pipelineInfo.pStages = shaderStages;
    pipelineInfo.pVertexInputState = &vertexInputInfo;
    pipelineInfo.pInputAssemblyState = &inputAssembly;
    pipelineInfo.pViewportState = &viewportState;
    pipelineInfo.pRasterizationState = &rasterizer;
    pipelineInfo.pMultisampleState = &multisampling;
    pipelineInfo.pDepthStencilState = nullptr; // No depth testing
    pipelineInfo.pColorBlendState = &colorBlending;
    pipelineInfo.pDynamicState = nullptr; // No dynamic state
    pipelineInfo.layout = m_pipelineLayout;
    pipelineInfo.renderPass = m_renderPass;
    pipelineInfo.subpass = 0; // Index of the subpass
    // basePipelineHandle/Index for deriving from existing pipeline (optimization)

    if (vkCreateGraphicsPipelines(m_device, VK_NULL_HANDLE, 1, &pipelineInfo, nullptr, &m_graphicsPipeline) != VK_SUCCESS)
    {
      throw std::runtime_error("Failed to create graphics pipeline");
    }

    // Shader modules can be destroyed after pipeline creation
    vkDestroyShaderModule(m_device, fragShaderModule, nullptr);
    vkDestroyShaderModule(m_device, vertShaderModule, nullptr);
  }

  // ============================================================================
  // SECTION 9: FRAMEBUFFERS
  // ============================================================================
  //
  // Framebuffers connect image views to render pass attachments.
  //
  // The render pass describes WHAT attachments look like (format, samples).
  // The framebuffer provides the ACTUAL images for those attachment slots.
  //
  // We create one framebuffer per swapchain image, each pointing to
  // a different image view.
  //
  // ============================================================================

  void RendererDemo::CreateFramebuffers()
  {
    // Clean up old framebuffers
    for (auto framebuffer : m_framebuffers)
    {
      vkDestroyFramebuffer(m_device, framebuffer, nullptr);
    }

    m_framebuffers.resize(m_swapchainImageViews.size());

    for (size_t i = 0; i < m_swapchainImageViews.size(); i++)
    {
      // Attachments in order matching render pass attachment descriptions
      VkImageView attachments[] = {m_swapchainImageViews[i]};

      VkFramebufferCreateInfo framebufferInfo{};
      framebufferInfo.sType = VK_STRUCTURE_TYPE_FRAMEBUFFER_CREATE_INFO;
      framebufferInfo.renderPass = m_renderPass; // Must be compatible
      framebufferInfo.attachmentCount = 1;
      framebufferInfo.pAttachments = attachments;
      framebufferInfo.width = m_swapchainExtent.width;
      framebufferInfo.height = m_swapchainExtent.height;
      framebufferInfo.layers = 1; // Non-array images

      if (vkCreateFramebuffer(m_device, &framebufferInfo, nullptr, &m_framebuffers[i]) != VK_SUCCESS)
      {
        throw std::runtime_error("Failed to create framebuffer");
      }
    }
  }

  // ============================================================================
  // SECTION 10: COMMAND POOL & COMMAND BUFFERS
  // ============================================================================
  //
  // Commands in Vulkan are not executed immediately. Instead, you record
  // them into command buffers, then submit those buffers to a queue.
  //
  // COMMAND POOL:
  //   Allocator for command buffers. Pools are tied to a specific queue family.
  //   RESET_COMMAND_BUFFER_BIT allows individual buffers to be reset and reused.
  //
  // COMMAND BUFFER:
  //   A recording of GPU commands. Can be:
  //   - PRIMARY: Submitted directly to queue
  //   - SECONDARY: Called from primary buffers (for reuse, multithreading)
  //
  // ============================================================================

  void RendererDemo::CreateCommandPool()
  {
    VkCommandPoolCreateInfo poolInfo{};
    poolInfo.sType = VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO;
    poolInfo.flags = VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT; // Buffers can be reset individually
    poolInfo.queueFamilyIndex = m_graphicsQueueFamily;                // For graphics commands

    if (vkCreateCommandPool(m_device, &poolInfo, nullptr, &m_commandPool) != VK_SUCCESS)
    {
      throw std::runtime_error("Failed to create command pool");
    }
  }

  void RendererDemo::CreateCommandBuffer()
  {
    VkCommandBufferAllocateInfo allocInfo{};
    allocInfo.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO;
    allocInfo.commandPool = m_commandPool;
    allocInfo.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY; // Can be submitted to queue directly
    allocInfo.commandBufferCount = 1;

    if (vkAllocateCommandBuffers(m_device, &allocInfo, &m_commandBuffer) != VK_SUCCESS)
    {
      throw std::runtime_error("Failed to allocate command buffers");
    }
  }

  // ============================================================================
  // SECTION 11: SYNCHRONIZATION OBJECTS
  // ============================================================================
  //
  // Vulkan requires explicit synchronization. Two main primitives:
  //
  // SEMAPHORES (GPU-GPU sync):
  //   Signal when an operation completes, other operations can wait on them.
  //   Used between queue submissions.
  //   - imageAvailableSemaphore: Swapchain image acquired, safe to render
  //   - renderFinishedSemaphore: Rendering done, safe to present
  //
  // FENCES (CPU-GPU sync):
  //   CPU can wait for GPU operations to complete.
  //   - inFlightFence: Ensures we don't record new commands while GPU is
  //                    still using the previous frame's command buffer.
  //   Created SIGNALED so first frame doesn't wait forever.
  //
  // ============================================================================

  void RendererDemo::CreateSyncObjects()
  {
    VkSemaphoreCreateInfo semaphoreInfo{};
    semaphoreInfo.sType = VK_STRUCTURE_TYPE_SEMAPHORE_CREATE_INFO;

    VkFenceCreateInfo fenceInfo{};
    fenceInfo.sType = VK_STRUCTURE_TYPE_FENCE_CREATE_INFO;
    fenceInfo.flags = VK_FENCE_CREATE_SIGNALED_BIT; // Start signaled for first frame

    if (vkCreateSemaphore(m_device, &semaphoreInfo, nullptr, &m_imageAvailableSemaphore) != VK_SUCCESS ||
        vkCreateSemaphore(m_device, &semaphoreInfo, nullptr, &m_renderFinishedSemaphore) != VK_SUCCESS ||
        vkCreateFence(m_device, &fenceInfo, nullptr, &m_inFlightFence) != VK_SUCCESS)
    {
      throw std::runtime_error("Failed to create synchronization objects");
    }
  }

  // ============================================================================
  // SECTION 12: VERTEX & INDEX BUFFERS
  // ============================================================================
  //
  // Buffers hold data on the GPU. For rendering, we need:
  //   - Vertex Buffer: Per-vertex data (position, color, normals, UVs)
  //   - Index Buffer: Indices into vertex buffer to form triangles
  //
  // MEMORY TYPES:
  //   - HOST_VISIBLE: CPU can write to it directly (slow for GPU access)
  //   - DEVICE_LOCAL: Fast GPU memory (CPU can't access directly)
  //
  // STAGING BUFFER PATTERN:
  //   1. Create HOST_VISIBLE staging buffer
  //   2. Copy data from CPU to staging buffer (memcpy)
  //   3. Create DEVICE_LOCAL destination buffer
  //   4. Copy from staging to destination (GPU command)
  //   5. Delete staging buffer
  //
  // This gives us fast GPU access while only briefly using slow memory.
  //
  // ============================================================================

  void RendererDemo::CreateVertexBuffer()
  {
    if (m_vertexBuffer != VK_NULL_HANDLE)
    {
      vkDestroyBuffer(m_device, m_vertexBuffer, nullptr);
      vkFreeMemory(m_device, m_vertexBufferMemory, nullptr);
    }

    VkDeviceSize bufferSize = sizeof(float) * m_vertices.size();

    // Create staging buffer (CPU-accessible)
    VkBuffer stagingBuffer;
    VkDeviceMemory stagingBufferMemory;
    CreateBuffer(bufferSize, VK_BUFFER_USAGE_TRANSFER_SRC_BIT,
                 VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT,
                 stagingBuffer, stagingBufferMemory);

    // Copy vertex data to staging buffer
    void *data;
    vkMapMemory(m_device, stagingBufferMemory, 0, bufferSize, 0, &data);
    memcpy(data, m_vertices.data(), (size_t)bufferSize);
    vkUnmapMemory(m_device, stagingBufferMemory);

    // Create device-local buffer (fast GPU memory)
    CreateBuffer(bufferSize, VK_BUFFER_USAGE_TRANSFER_DST_BIT | VK_BUFFER_USAGE_VERTEX_BUFFER_BIT,
                 VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT,
                 m_vertexBuffer, m_vertexBufferMemory);

    // Copy from staging to device-local
    CopyBuffer(stagingBuffer, m_vertexBuffer, bufferSize);

    // Clean up staging buffer
    vkDestroyBuffer(m_device, stagingBuffer, nullptr);
    vkFreeMemory(m_device, stagingBufferMemory, nullptr);
  }

  void RendererDemo::CreateIndexBuffer()
  {
    if (m_indexBuffer != VK_NULL_HANDLE)
    {
      vkDestroyBuffer(m_device, m_indexBuffer, nullptr);
      vkFreeMemory(m_device, m_indexBufferMemory, nullptr);
    }

    VkDeviceSize bufferSize = sizeof(uint32_t) * m_indices.size();

    // Same staging pattern as vertex buffer
    VkBuffer stagingBuffer;
    VkDeviceMemory stagingBufferMemory;
    CreateBuffer(bufferSize, VK_BUFFER_USAGE_TRANSFER_SRC_BIT,
                 VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT,
                 stagingBuffer, stagingBufferMemory);

    void *data;
    vkMapMemory(m_device, stagingBufferMemory, 0, bufferSize, 0, &data);
    memcpy(data, m_indices.data(), (size_t)bufferSize);
    vkUnmapMemory(m_device, stagingBufferMemory);

    CreateBuffer(bufferSize, VK_BUFFER_USAGE_TRANSFER_DST_BIT | VK_BUFFER_USAGE_INDEX_BUFFER_BIT,
                 VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT,
                 m_indexBuffer, m_indexBufferMemory);

    CopyBuffer(stagingBuffer, m_indexBuffer, bufferSize);

    vkDestroyBuffer(m_device, stagingBuffer, nullptr);
    vkFreeMemory(m_device, stagingBufferMemory, nullptr);
  }

  // ============================================================================
  // SECTION 13: COMMAND RECORDING
  // ============================================================================
  //
  // This is where we tell the GPU what to actually draw.
  //
  // COMMAND STRUCTURE:
  //   1. Begin command buffer
  //   2. Begin render pass (start rendering to framebuffer)
  //   3. Bind pipeline (which shaders/state to use)
  //   4. Bind vertex/index buffers (geometry data)
  //   5. Draw command (actually render)
  //   6. End render pass
  //   7. End command buffer
  //
  // The command buffer is then submitted to the queue for execution.
  //
  // ============================================================================

  void RendererDemo::RecordCommandBuffer(VkCommandBuffer commandBuffer, uint32_t imageIndex)
  {
    VkCommandBufferBeginInfo beginInfo{};
    beginInfo.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO;
    // flags could include ONE_TIME_SUBMIT_BIT if buffer is recorded once

    if (vkBeginCommandBuffer(commandBuffer, &beginInfo) != VK_SUCCESS)
    {
      throw std::runtime_error("Failed to begin recording command buffer");
    }

    // Begin the render pass
    VkRenderPassBeginInfo renderPassInfo{};
    renderPassInfo.sType = VK_STRUCTURE_TYPE_RENDER_PASS_BEGIN_INFO;
    renderPassInfo.renderPass = m_renderPass;
    renderPassInfo.framebuffer = m_framebuffers[imageIndex]; // Which framebuffer to render to
    renderPassInfo.renderArea.offset = {0, 0};
    renderPassInfo.renderArea.extent = m_swapchainExtent;

    // Clear color for VK_ATTACHMENT_LOAD_OP_CLEAR
    VkClearValue clearColor = {{{0.0f, 0.0f, 0.0f, 1.0f}}}; // Black
    renderPassInfo.clearValueCount = 1;
    renderPassInfo.pClearValues = &clearColor;

    // INLINE means commands are in this buffer (not calling secondary buffers)
    vkCmdBeginRenderPass(commandBuffer, &renderPassInfo, VK_SUBPASS_CONTENTS_INLINE);

    // Bind the graphics pipeline
    vkCmdBindPipeline(commandBuffer, VK_PIPELINE_BIND_POINT_GRAPHICS, m_graphicsPipeline);

    // Bind vertex buffer to binding point 0
    VkBuffer vertexBuffers[] = {m_vertexBuffer};
    VkDeviceSize offsets[] = {0};
    vkCmdBindVertexBuffers(commandBuffer, 0, 1, vertexBuffers, offsets);

    // Bind index buffer
    vkCmdBindIndexBuffer(commandBuffer, m_indexBuffer, 0, VK_INDEX_TYPE_UINT32);

    // Draw indexed: indexCount, instanceCount, firstIndex, vertexOffset, firstInstance
    vkCmdDrawIndexed(commandBuffer, static_cast<uint32_t>(m_indices.size()), 1, 0, 0, 0);

    vkCmdEndRenderPass(commandBuffer);

    if (vkEndCommandBuffer(commandBuffer) != VK_SUCCESS)
    {
      throw std::runtime_error("Failed to record command buffer");
    }
  }

  // ============================================================================
  // SECTION 14: UTILITY FUNCTIONS
  // ============================================================================

  VkShaderModule RendererDemo::CreateShaderModule(const std::vector<char> &code)
  {
    VkShaderModuleCreateInfo createInfo{};
    createInfo.sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
    createInfo.codeSize = code.size();
    createInfo.pCode = reinterpret_cast<const uint32_t *>(code.data());

    VkShaderModule shaderModule;
    if (vkCreateShaderModule(m_device, &createInfo, nullptr, &shaderModule) != VK_SUCCESS)
    {
      throw std::runtime_error("Failed to create shader module");
    }

    return shaderModule;
  }

  // Find a memory type that satisfies both the type filter and property requirements
  uint32_t RendererDemo::FindMemoryType(uint32_t typeFilter, VkMemoryPropertyFlags properties)
  {
    VkPhysicalDeviceMemoryProperties memProperties;
    vkGetPhysicalDeviceMemoryProperties(m_physicalDevice, &memProperties);

    for (uint32_t i = 0; i < memProperties.memoryTypeCount; i++)
    {
      // typeFilter is a bitmask - bit i is set if memory type i is suitable
      // Also check that all required properties are present
      if ((typeFilter & (1 << i)) && (memProperties.memoryTypes[i].propertyFlags & properties) == properties)
      {
        return i;
      }
    }

    throw std::runtime_error("Failed to find suitable memory type");
  }

  // Generic buffer creation helper
  void RendererDemo::CreateBuffer(VkDeviceSize size, VkBufferUsageFlags usage, VkMemoryPropertyFlags properties, VkBuffer &buffer, VkDeviceMemory &bufferMemory)
  {
    // Create the buffer object (just metadata, no memory yet)
    VkBufferCreateInfo bufferInfo{};
    bufferInfo.sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO;
    bufferInfo.size = size;
    bufferInfo.usage = usage;
    bufferInfo.sharingMode = VK_SHARING_MODE_EXCLUSIVE;

    if (vkCreateBuffer(m_device, &bufferInfo, nullptr, &buffer) != VK_SUCCESS)
    {
      throw std::runtime_error("Failed to create buffer");
    }

    // Query memory requirements (size, alignment, compatible memory types)
    VkMemoryRequirements memRequirements;
    vkGetBufferMemoryRequirements(m_device, buffer, &memRequirements);

    // Allocate memory
    VkMemoryAllocateInfo allocInfo{};
    allocInfo.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
    allocInfo.allocationSize = memRequirements.size;
    allocInfo.memoryTypeIndex = FindMemoryType(memRequirements.memoryTypeBits, properties);

    if (vkAllocateMemory(m_device, &allocInfo, nullptr, &bufferMemory) != VK_SUCCESS)
    {
      throw std::runtime_error("Failed to allocate buffer memory");
    }

    // Bind memory to buffer
    vkBindBufferMemory(m_device, buffer, bufferMemory, 0);
  }

  // Copy data between buffers using GPU command
  void RendererDemo::CopyBuffer(VkBuffer srcBuffer, VkBuffer dstBuffer, VkDeviceSize size)
  {
    // Allocate a temporary command buffer for the copy
    VkCommandBufferAllocateInfo allocInfo{};
    allocInfo.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO;
    allocInfo.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY;
    allocInfo.commandPool = m_commandPool;
    allocInfo.commandBufferCount = 1;

    VkCommandBuffer commandBuffer;
    vkAllocateCommandBuffers(m_device, &allocInfo, &commandBuffer);

    // Record the copy command
    VkCommandBufferBeginInfo beginInfo{};
    beginInfo.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO;
    beginInfo.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT; // Used once then discarded

    vkBeginCommandBuffer(commandBuffer, &beginInfo);

    VkBufferCopy copyRegion{};
    copyRegion.size = size;
    vkCmdCopyBuffer(commandBuffer, srcBuffer, dstBuffer, 1, &copyRegion);

    vkEndCommandBuffer(commandBuffer);

    // Submit and wait for completion
    VkSubmitInfo submitInfo{};
    submitInfo.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO;
    submitInfo.commandBufferCount = 1;
    submitInfo.pCommandBuffers = &commandBuffer;

    vkQueueSubmit(m_graphicsQueue, 1, &submitInfo, VK_NULL_HANDLE);
    vkQueueWaitIdle(m_graphicsQueue); // Wait for copy to complete

    vkFreeCommandBuffers(m_device, m_commandPool, 1, &commandBuffer);
  }

  // ============================================================================
  // SECTION 15: CLEANUP
  // ============================================================================
  //
  // Vulkan requires explicit destruction of all objects in reverse order
  // of creation. Objects that depend on others must be destroyed first.
  //
  // General rule: destroy in reverse order of creation dependencies.
  //
  // ============================================================================

  void RendererDemo::Cleanup()
  {
    if (m_device != VK_NULL_HANDLE)
    {
      // Wait for GPU to finish all work
      vkDeviceWaitIdle(m_device);

      // Destroy sync objects
      if (m_inFlightFence != VK_NULL_HANDLE)
        vkDestroyFence(m_device, m_inFlightFence, nullptr);
      if (m_renderFinishedSemaphore != VK_NULL_HANDLE)
        vkDestroySemaphore(m_device, m_renderFinishedSemaphore, nullptr);
      if (m_imageAvailableSemaphore != VK_NULL_HANDLE)
        vkDestroySemaphore(m_device, m_imageAvailableSemaphore, nullptr);

      // Destroy buffers
      if (m_indexBuffer != VK_NULL_HANDLE)
        vkDestroyBuffer(m_device, m_indexBuffer, nullptr);
      if (m_indexBufferMemory != VK_NULL_HANDLE)
        vkFreeMemory(m_device, m_indexBufferMemory, nullptr);
      if (m_vertexBuffer != VK_NULL_HANDLE)
        vkDestroyBuffer(m_device, m_vertexBuffer, nullptr);
      if (m_vertexBufferMemory != VK_NULL_HANDLE)
        vkFreeMemory(m_device, m_vertexBufferMemory, nullptr);

      // Destroy command pool (frees all allocated command buffers)
      if (m_commandPool != VK_NULL_HANDLE)
        vkDestroyCommandPool(m_device, m_commandPool, nullptr);

      // Destroy framebuffers
      for (auto framebuffer : m_framebuffers)
      {
        vkDestroyFramebuffer(m_device, framebuffer, nullptr);
      }

      // Destroy pipeline and layout
      if (m_graphicsPipeline != VK_NULL_HANDLE)
        vkDestroyPipeline(m_device, m_graphicsPipeline, nullptr);
      if (m_pipelineLayout != VK_NULL_HANDLE)
        vkDestroyPipelineLayout(m_device, m_pipelineLayout, nullptr);

      // Destroy render pass
      if (m_renderPass != VK_NULL_HANDLE)
        vkDestroyRenderPass(m_device, m_renderPass, nullptr);

      // Destroy image views
      for (auto imageView : m_swapchainImageViews)
      {
        vkDestroyImageView(m_device, imageView, nullptr);
      }

      // Destroy swapchain
      if (m_swapchain != VK_NULL_HANDLE)
        vkDestroySwapchainKHR(m_device, m_swapchain, nullptr);

      // Destroy logical device
      vkDestroyDevice(m_device, nullptr);
    }

    // Destroy surface (before instance)
    if (m_surface != VK_NULL_HANDLE)
      vkDestroySurfaceKHR(m_instance, m_surface, nullptr);

    // Destroy instance last
    if (m_instance != VK_NULL_HANDLE)
      vkDestroyInstance(m_instance, nullptr);
  }

} // namespace PrimordialPower::Renderers::Obsidian

// ============================================================================
// C EXPORTS - For calling from other languages (C#, etc.)
// ============================================================================

// using namespace PrimordialPower::Renderers::Obsidian;

// EXPORT void Renderer_Init(Renderer *renderer, intptr_t display, uint64_t window)
// {
//   renderer->Init(display, window);
// }

// EXPORT void Renderer_LoadShaders(Renderer *renderer, const char *vertShadPath, const char *fragShadPath)
// {
//   renderer->LoadShaders(vertShadPath, fragShadPath);
// }

// EXPORT void Renderer_LoadMesh(Renderer *renderer, float *vertices, uint32_t vertexCount, uint32_t *indices, uint32_t indexCount)
// {
//   renderer->LoadMesh(vertices, vertexCount, indices, indexCount);
// }

// EXPORT void Renderer_Render(Renderer *renderer)
// {
//   renderer->Render();
// }
