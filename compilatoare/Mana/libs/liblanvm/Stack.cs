using System.Runtime.InteropServices;

namespace LAN.VirtualMachine;


public unsafe class Stack
{
  public const uint DefaultSize = 1048576;

  private byte* TopAddress;
  private byte* BottomAddress;
  private byte* FrameAddress;
  private byte[] Array;
  private GCHandle Handle;
  public Stack(uint size = DefaultSize)
  {
    Array = new byte[size];
    Handle = GCHandle.Alloc(Array, GCHandleType.Pinned);
    TopAddress = (byte*)Handle.AddrOfPinnedObject();
    BottomAddress = (byte*)Handle.AddrOfPinnedObject();
    FrameAddress = BottomAddress;
  }

  public uint Allocated => (uint)(TopAddress - BottomAddress);

  public void* Top(uint size)
  {
    return TopAddress - size;
  }

  public void* Pop(uint size)
  {
    TopAddress -= size;
    return TopAddress;
  }

  public void* Push(uint size)
  {
    void* dest = TopAddress;
    TopAddress += size;
    return dest;
  }

  public void EnterFrame()
  {
    var dest = (byte**)Push(sizeof(ulong));
    *dest = FrameAddress;
    FrameAddress = TopAddress;
  }

  public void ExitFrame()
  {
    TopAddress = FrameAddress;
    var prev = (byte**)Pop(sizeof(ulong));
    FrameAddress = *prev;
  }
}
