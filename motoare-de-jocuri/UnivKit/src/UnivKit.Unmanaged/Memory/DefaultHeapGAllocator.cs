using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace UnivKit.Unmanaged.Memory;

public class DefaultHeapGAllocator : IAllocator
{
  public nint Allocate(long size)
  {
    return Marshal.AllocHGlobal((int)size);
  }

  public void Free(nint adress)
  {
    Marshal.FreeHGlobal(adress);
  }
}
