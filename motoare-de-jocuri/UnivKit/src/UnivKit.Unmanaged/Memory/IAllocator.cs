namespace UnivKit.Unmanaged.Memory;

public interface IAllocator
{
  nint Allocate(long size);
  void Free(nint adress);
}