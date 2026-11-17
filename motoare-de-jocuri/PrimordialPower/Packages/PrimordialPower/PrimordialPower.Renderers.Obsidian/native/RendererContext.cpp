#include "RendererContext.hpp"

#include "lib/All.h"

using namespace PrimordialPower::Renderers::Obsidian;

RendererContext::RendererContext(const Data::ContextData &data)
{
  _data = data;
}
//
//
//
//
//
//
// Destroys all per-viewport Vulkan resources in reverse creation order.
//
RendererContext::~RendererContext()
{
  if (_data.Device == nullptr || _data.Device->Device == VK_NULL_HANDLE)
    return;

  VkDevice device = _data.Device->Device;
  vkDeviceWaitIdle(device);

  // Sync objects
  if (_data.InFlightFence != VK_NULL_HANDLE)
    vkDestroyFence(device, _data.InFlightFence, nullptr);
  if (_data.RenderFinishedSemaphore != VK_NULL_HANDLE)
    vkDestroySemaphore(device, _data.RenderFinishedSemaphore, nullptr);
  if (_data.ImageAvailableSemaphore != VK_NULL_HANDLE)
    vkDestroySemaphore(device, _data.ImageAvailableSemaphore, nullptr);

  // Command pool (frees all allocated command buffers)
  if (_data.CommandPool != VK_NULL_HANDLE)
    vkDestroyCommandPool(device, _data.CommandPool, nullptr);

  // Framebuffers
  for (auto fb : _data.Framebuffers)
    vkDestroyFramebuffer(device, fb, nullptr);

  // Render pass
  if (_data.RenderPass != VK_NULL_HANDLE)
    vkDestroyRenderPass(device, _data.RenderPass, nullptr);

  // Image views
  for (auto iv : _data.ImageViews)
    vkDestroyImageView(device, iv, nullptr);

  // Swapchain
  if (_data.Swapchain != VK_NULL_HANDLE)
    vkDestroySwapchainKHR(device, _data.Swapchain, nullptr);

  // Surface (needs VkInstance, not VkDevice)
  if (_data.Surface != VK_NULL_HANDLE)
    vkDestroySurfaceKHR(_data.Instance, _data.Surface, nullptr);
}
//
//
//
//
//
//
// Renders a single frame: wait for previous frame, acquire image,
// record commands, submit to GPU, present to screen.
//
void RendererContext::DrawFrame()
{
  VkDevice device = _data.Device->Device;

  // Step 1: Wait for previous frame to finish
  vkWaitForFences(device, 1, &_data.InFlightFence, VK_TRUE, UINT64_MAX);

  // Step 2: Acquire next image from swapchain
  uint32_t imageIndex;
  VkResult result = vkAcquireNextImageKHR(device, _data.Swapchain, UINT64_MAX,
                                          _data.ImageAvailableSemaphore, VK_NULL_HANDLE, &imageIndex);

  if (result == VK_ERROR_OUT_OF_DATE_KHR)
  {
    RecreateSwapchain(_data.Extent.width, _data.Extent.height);
    return;
  }
  else if (result != VK_SUCCESS && result != VK_SUBOPTIMAL_KHR)
  {
    throw std::runtime_error("Failed to acquire swap chain image");
  }

  vkResetFences(device, 1, &_data.InFlightFence);

  // Step 3: Record draw commands
  vkResetCommandBuffer(_data.CommandBuffer, 0);
  RecordCommandBuffer(_data.CommandBuffer, imageIndex);

  // Step 4: Submit to GPU
  VkSubmitInfo submitInfo{};
  submitInfo.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO;

  VkSemaphore waitSemaphores[] = {_data.ImageAvailableSemaphore};
  VkPipelineStageFlags waitStages[] = {VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT};
  submitInfo.waitSemaphoreCount = 1;
  submitInfo.pWaitSemaphores = waitSemaphores;
  submitInfo.pWaitDstStageMask = waitStages;
  submitInfo.commandBufferCount = 1;
  submitInfo.pCommandBuffers = &_data.CommandBuffer;

  VkSemaphore signalSemaphores[] = {_data.RenderFinishedSemaphore};
  submitInfo.signalSemaphoreCount = 1;
  submitInfo.pSignalSemaphores = signalSemaphores;

  if (vkQueueSubmit(_data.Device->GraphicsQueue, 1, &submitInfo, _data.InFlightFence) != VK_SUCCESS)
  {
    throw std::runtime_error("Failed to submit draw command buffer");
  }

  // Step 5: Present to screen
  VkPresentInfoKHR presentInfo{};
  presentInfo.sType = VK_STRUCTURE_TYPE_PRESENT_INFO_KHR;
  presentInfo.waitSemaphoreCount = 1;
  presentInfo.pWaitSemaphores = signalSemaphores;

  VkSwapchainKHR swapchains[] = {_data.Swapchain};
  presentInfo.swapchainCount = 1;
  presentInfo.pSwapchains = swapchains;
  presentInfo.pImageIndices = &imageIndex;

  result = vkQueuePresentKHR(_data.Device->PresentQueue, &presentInfo);

  if (result == VK_ERROR_OUT_OF_DATE_KHR || result == VK_SUBOPTIMAL_KHR)
  {
    RecreateSwapchain(_data.Extent.width, _data.Extent.height);
  }
  else if (result != VK_SUCCESS)
  {
    throw std::runtime_error("Failed to present swap chain image");
  }
}
//
//
//
//
//
//
// Records commands for a single frame.
// Currently just clears the screen to a color (no graphics pipeline yet).
//
void RendererContext::RecordCommandBuffer(VkCommandBuffer commandBuffer, uint32_t imageIndex)
{
  VkCommandBufferBeginInfo beginInfo{};
  beginInfo.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO;

  if (vkBeginCommandBuffer(commandBuffer, &beginInfo) != VK_SUCCESS)
  {
    throw std::runtime_error("Failed to begin recording command buffer");
  }

  VkRenderPassBeginInfo renderPassInfo{};
  renderPassInfo.sType = VK_STRUCTURE_TYPE_RENDER_PASS_BEGIN_INFO;
  renderPassInfo.renderPass = _data.RenderPass;
  renderPassInfo.framebuffer = _data.Framebuffers[imageIndex];
  renderPassInfo.renderArea.offset = {0, 0};
  renderPassInfo.renderArea.extent = _data.Extent;

  // Clear to a dark teal color so it's obvious the renderer is working
  VkClearValue clearColor = {{{0.0f, 0.1f, 0.15f, 1.0f}}};
  renderPassInfo.clearValueCount = 1;
  renderPassInfo.pClearValues = &clearColor;

  vkCmdBeginRenderPass(commandBuffer, &renderPassInfo, VK_SUBPASS_CONTENTS_INLINE);

  // No pipeline bound yet - just clearing.
  // When a graphics pipeline is added later, it would be bound here:
  //   vkCmdBindPipeline(commandBuffer, VK_PIPELINE_BIND_POINT_GRAPHICS, pipeline);
  //   vkCmdDraw(...);

  vkCmdEndRenderPass(commandBuffer);

  if (vkEndCommandBuffer(commandBuffer) != VK_SUCCESS)
  {
    throw std::runtime_error("Failed to record command buffer");
  }
}
//
//
//
//
//
//
// Destroys swapchain-dependent resources (called before recreating them).
//
void RendererContext::CleanupSwapchain()
{
  VkDevice device = _data.Device->Device;

  for (auto fb : _data.Framebuffers)
    vkDestroyFramebuffer(device, fb, nullptr);
  _data.Framebuffers.clear();

  if (_data.RenderPass != VK_NULL_HANDLE)
  {
    vkDestroyRenderPass(device, _data.RenderPass, nullptr);
    _data.RenderPass = VK_NULL_HANDLE;
  }

  for (auto iv : _data.ImageViews)
    vkDestroyImageView(device, iv, nullptr);
  _data.ImageViews.clear();

  if (_data.Swapchain != VK_NULL_HANDLE)
  {
    vkDestroySwapchainKHR(device, _data.Swapchain, nullptr);
    _data.Swapchain = VK_NULL_HANDLE;
  }
}
//
//
//
//
//
//
// Recreates the swapchain and dependent resources when the window is resized.
// The command pool and sync objects are reused - only the swapchain, image views,
// render pass, and framebuffers need to be recreated.
//
void RendererContext::RecreateSwapchain(uint32_t width, uint32_t height)
{
  if (width == 0 || height == 0)
    return; // Window is minimized, don't recreate

  VkDevice device = _data.Device->Device;
  VkPhysicalDevice physicalDevice = _data.Device->PhysicalDevice;

  vkDeviceWaitIdle(device);

  CleanupSwapchain();

  // Free old command buffer (reuse the pool)
  if (_data.CommandBuffer != VK_NULL_HANDLE)
  {
    vkFreeCommandBuffers(device, _data.CommandPool, 1, &_data.CommandBuffer);
    _data.CommandBuffer = VK_NULL_HANDLE;
  }

  // Re-query surface capabilities
  VkSurfaceCapabilitiesKHR capabilities;
  vkGetPhysicalDeviceSurfaceCapabilitiesKHR(physicalDevice, _data.Surface, &capabilities);

  // Get supported formats
  uint32_t formatCount;
  vkGetPhysicalDeviceSurfaceFormatsKHR(physicalDevice, _data.Surface, &formatCount, nullptr);
  std::vector<VkSurfaceFormatKHR> formats(formatCount);
  vkGetPhysicalDeviceSurfaceFormatsKHR(physicalDevice, _data.Surface, &formatCount, formats.data());

  // Get supported present modes
  uint32_t presentModeCount;
  vkGetPhysicalDeviceSurfacePresentModesKHR(physicalDevice, _data.Surface, &presentModeCount, nullptr);
  std::vector<VkPresentModeKHR> presentModes(presentModeCount);
  vkGetPhysicalDeviceSurfacePresentModesKHR(physicalDevice, _data.Surface, &presentModeCount, presentModes.data());

  // Choose format
  VkSurfaceFormatKHR surfaceFormat = formats[0];
  for (const auto &format : formats)
  {
    if (format.format == VK_FORMAT_B8G8R8A8_SRGB && format.colorSpace == VK_COLOR_SPACE_SRGB_NONLINEAR_KHR)
    {
      surfaceFormat = format;
      break;
    }
  }
  _data.Format = surfaceFormat.format;

  // Choose present mode
  VkPresentModeKHR presentMode = VK_PRESENT_MODE_FIFO_KHR;
  for (const auto &mode : presentModes)
  {
    if (mode == VK_PRESENT_MODE_MAILBOX_KHR)
    {
      presentMode = mode;
      break;
    }
  }

  // Choose extent
  if (capabilities.currentExtent.width != std::numeric_limits<uint32_t>::max())
  {
    _data.Extent = capabilities.currentExtent;
  }
  else
  {
    _data.Extent = {width, height};
    _data.Extent.width = std::clamp(_data.Extent.width, capabilities.minImageExtent.width, capabilities.maxImageExtent.width);
    _data.Extent.height = std::clamp(_data.Extent.height, capabilities.minImageExtent.height, capabilities.maxImageExtent.height);
  }

  // Image count
  uint32_t imageCount = capabilities.minImageCount + 1;
  if (capabilities.maxImageCount > 0 && imageCount > capabilities.maxImageCount)
  {
    imageCount = capabilities.maxImageCount;
  }

  // Recreate swapchain
  VkSwapchainCreateInfoKHR swapchainCreateInfo{};
  swapchainCreateInfo.sType = VK_STRUCTURE_TYPE_SWAPCHAIN_CREATE_INFO_KHR;
  swapchainCreateInfo.surface = _data.Surface;
  swapchainCreateInfo.minImageCount = imageCount;
  swapchainCreateInfo.imageFormat = surfaceFormat.format;
  swapchainCreateInfo.imageColorSpace = surfaceFormat.colorSpace;
  swapchainCreateInfo.imageExtent = _data.Extent;
  swapchainCreateInfo.imageArrayLayers = 1;
  swapchainCreateInfo.imageUsage = VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT;

  uint32_t queueFamilyIndices[] = {_data.Device->GraphicsQueueFamily, _data.Device->PresentQueueFamily};
  if (_data.Device->GraphicsQueueFamily != _data.Device->PresentQueueFamily)
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

  if (vkCreateSwapchainKHR(device, &swapchainCreateInfo, nullptr, &_data.Swapchain) != VK_SUCCESS)
  {
    throw std::runtime_error("Failed to recreate swap chain");
  }

  // Get swapchain images
  vkGetSwapchainImagesKHR(device, _data.Swapchain, &imageCount, nullptr);
  _data.Images.resize(imageCount);
  vkGetSwapchainImagesKHR(device, _data.Swapchain, &imageCount, _data.Images.data());

  // Recreate image views
  _data.ImageViews.resize(_data.Images.size());
  for (size_t i = 0; i < _data.Images.size(); i++)
  {
    VkImageViewCreateInfo createInfo{};
    createInfo.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
    createInfo.image = _data.Images[i];
    createInfo.viewType = VK_IMAGE_VIEW_TYPE_2D;
    createInfo.format = _data.Format;
    createInfo.components.r = VK_COMPONENT_SWIZZLE_IDENTITY;
    createInfo.components.g = VK_COMPONENT_SWIZZLE_IDENTITY;
    createInfo.components.b = VK_COMPONENT_SWIZZLE_IDENTITY;
    createInfo.components.a = VK_COMPONENT_SWIZZLE_IDENTITY;
    createInfo.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
    createInfo.subresourceRange.baseMipLevel = 0;
    createInfo.subresourceRange.levelCount = 1;
    createInfo.subresourceRange.baseArrayLayer = 0;
    createInfo.subresourceRange.layerCount = 1;

    if (vkCreateImageView(device, &createInfo, nullptr, &_data.ImageViews[i]) != VK_SUCCESS)
    {
      throw std::runtime_error("Failed to create image views");
    }
  }

  // Recreate render pass
  VkAttachmentDescription colorAttachment{};
  colorAttachment.format = _data.Format;
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

  if (vkCreateRenderPass(device, &renderPassInfo, nullptr, &_data.RenderPass) != VK_SUCCESS)
  {
    throw std::runtime_error("Failed to recreate render pass");
  }

  // Recreate framebuffers
  _data.Framebuffers.resize(_data.ImageViews.size());
  for (size_t i = 0; i < _data.ImageViews.size(); i++)
  {
    VkImageView attachments[] = {_data.ImageViews[i]};

    VkFramebufferCreateInfo framebufferInfo{};
    framebufferInfo.sType = VK_STRUCTURE_TYPE_FRAMEBUFFER_CREATE_INFO;
    framebufferInfo.renderPass = _data.RenderPass;
    framebufferInfo.attachmentCount = 1;
    framebufferInfo.pAttachments = attachments;
    framebufferInfo.width = _data.Extent.width;
    framebufferInfo.height = _data.Extent.height;
    framebufferInfo.layers = 1;

    if (vkCreateFramebuffer(device, &framebufferInfo, nullptr, &_data.Framebuffers[i]) != VK_SUCCESS)
    {
      throw std::runtime_error("Failed to recreate framebuffer");
    }
  }

  // Reallocate command buffer from existing pool
  VkCommandBufferAllocateInfo allocInfo{};
  allocInfo.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO;
  allocInfo.commandPool = _data.CommandPool;
  allocInfo.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY;
  allocInfo.commandBufferCount = 1;

  if (vkAllocateCommandBuffers(device, &allocInfo, &_data.CommandBuffer) != VK_SUCCESS)
  {
    throw std::runtime_error("Failed to reallocate command buffer");
  }
}
