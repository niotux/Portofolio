namespace QuantiX.Engine.Native.Memory;

public interface IAllocator
{
  nint Allocate(long size);
  void Free(nint adress);
}