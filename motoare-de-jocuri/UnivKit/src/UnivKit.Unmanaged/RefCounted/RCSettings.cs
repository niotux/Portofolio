using UnivKit.Unmanaged.Memory;

namespace UnivKit.Unmanaged.RefCounted;

public static class RCSettings
{
  private static bool _allocatorUsed = false;
  private static IAllocator _allocator = new DefaultHeapGAllocator();
  public static IAllocator Allocator
  {
    get
    {
      _allocatorUsed = true;
      return _allocator;
    }
    set
    {
      if (_allocatorUsed)
      {
        throw new InvalidOperationException("Cannot set the allocator after getting it!");
      }

      _allocator = value;
    }
  }
}