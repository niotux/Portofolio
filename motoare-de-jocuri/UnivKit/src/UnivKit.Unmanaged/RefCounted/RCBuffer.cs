using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace UnivKit.Unmanaged.RefCounted;

[StructLayout(LayoutKind.Sequential)]
public struct RCBuffer : IRefCounted<RCBuffer>
{
  public nint Adress { get; private set; }
  private readonly unsafe ref long refCount => ref *(long*)Adress;
  public readonly unsafe long RefCount => *(long*)Adress;
  public readonly unsafe void* Data => this.Adress != 0
    ? (void*)(this.Adress + sizeof(long))
    : throw new NullReferenceException();
  public RCBuffer(long size)
  {
    nint stagingAdress = RCSettings.Allocator.Allocate(sizeof(long) + size);
    unsafe
    {
      *(long*)stagingAdress = 1;
    }

    this.Adress = stagingAdress;
  }

  internal RCBuffer(nint adress)
  {
    this.Adress = adress;
  }



  public static RCBuffer operator ++(RCBuffer buffer)
  {
    if (buffer.Adress == 0)
    {
      return default;
    }

    Interlocked.Increment(ref buffer.refCount);
    return buffer;
  }

  public static RCBuffer operator --(RCBuffer buffer)
  {
    if (buffer.Adress == 0)
    {
      return default;
    }

    long now = Interlocked.Decrement(ref buffer.refCount);
    if (now == 0)
    {
      RCSettings.Allocator.Free(buffer.Adress);
      buffer.Adress = 0;
    }
    return buffer;
  }

  public void Dispose() => this--;



  public bool Equals(RCBuffer other) => this.Adress == other.Adress;

  public override bool Equals(object? o)
  {
    if (o is not RCBuffer buffer) return false;
    return this.Equals(buffer);
  }

  public override int GetHashCode()
  {
    return HashCode.Combine(this.Adress);
  }



  public static bool operator ==(RCBuffer left, RCBuffer right) => left.Equals(right);
  public static bool operator !=(RCBuffer left, RCBuffer right) => !left.Equals(right);

  public static explicit operator RCBuffer(nint ptr) => new(ptr);
}