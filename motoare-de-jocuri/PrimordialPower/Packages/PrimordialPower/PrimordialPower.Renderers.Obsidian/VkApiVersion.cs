namespace PrimordialPower.Renderers.Obsidian;

// Vulkan API version encoding: VK_MAKE_API_VERSION(0, major, minor, 0)
// = (major << 22) | (minor << 12)
public enum VkApiVersion : uint
{
  Vk1_0 = (1 << 22),
  Vk1_1 = (1 << 22) | (1 << 12),
  Vk1_2 = (1 << 22) | (2 << 12),
  Vk1_3 = (1 << 22) | (3 << 12),
  Vk1_4 = (1 << 22) | (4 << 12),
}
