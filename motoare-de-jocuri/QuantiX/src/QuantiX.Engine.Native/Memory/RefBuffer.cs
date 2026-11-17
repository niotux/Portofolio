using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace QuantiX.Engine.Native.Memory;

[StructLayout(LayoutKind.Sequential)]
public readonly struct RefBuffer : IRefCounted<RefBuffer>
{
  public nint Adress { get; }
  public unsafe ref long RefCount => ref *(long*)Adress;
  public unsafe void* Data => (void*)(this.Adress + sizeof(long));
  public RefBuffer(long size)
  {
    nint stagingAdress = RefSettings.Allocator.Allocate(sizeof(long) + size);
    unsafe
    {
      *(long*)stagingAdress = 1;
    }

    this.Adress = stagingAdress;
  }

  internal RefBuffer(nint adress)
  {
    this.Adress = adress;
  }



  public static RefBuffer Null => default;



  public RefBuffer Pass()
  {
    Interlocked.Increment(ref this.RefCount);
    return this;
  }

  public void Dispose()
  {
    long now = Interlocked.Decrement(ref this.RefCount);
    if (now == 0)
    {
      RefSettings.Allocator.Free(this.Adress);
    }
  }



  public bool Equals(RefBuffer other) => this.Adress == other.Adress;

  public override bool Equals(object? o)
  {
    if (o is not RefBuffer buffer) return false;
    return this.Equals(buffer);
  }

  public override int GetHashCode()
  {
    return HashCode.Combine(this.Adress);
  }



  public static bool operator ==(RefBuffer left, RefBuffer right) => left.Equals(right);
  public static bool operator !=(RefBuffer left, RefBuffer right) => !left.Equals(right);

  public static explicit operator RefBuffer(nint ptr) => new(ptr);
}