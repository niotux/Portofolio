using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace QuantiX.Engine.Native.Memory;

[StructLayout(LayoutKind.Sequential)]
public readonly struct Ref<T> : IRefCounted<Ref<T>>
  where T : unmanaged
{
  private readonly RefBuffer buffer;

  public Ref()
  {
    this.buffer = default;
  }

  public Ref(T val)
  {
    this = Box(val);
  }

  public Ref(RefBuffer buffer)
  {
    this.buffer = buffer;
  }

  public readonly unsafe ref T Value => ref *(T*)this.buffer.Data;

  public static Ref<T> New(Constructor<T>? ctor = null)
  {
    unsafe
    {
      RefBuffer obj = new((long)sizeof(T));
      ctor?.Invoke(ref *(T*)obj.Data);
      return new(obj);
    }
  }

  public readonly Ref<T> Pass()
  {
    this.buffer.Pass();
    return this;
  }

  public readonly Ref<T> Clone()
  {
    unsafe
    {
      RefBuffer buff = new((long)sizeof(T));
      Unsafe.CopyBlock(buff.Data, this.buffer.Data, (uint)sizeof(T));
      return new(buff);
    }
  }

  public readonly void Dispose() => this.buffer.Dispose();
  public readonly bool Equals(Ref<T> other) => this.buffer.Equals(other.buffer);
  public readonly override bool Equals(object? obj) => this.buffer.Equals(obj);
  public readonly override int GetHashCode() => this.buffer.GetHashCode();
  public readonly override string? ToString() => this.Value.ToString();

  public static Ref<T> Box(T val)
  {
    unsafe
    {
      RefBuffer buff = new((long)sizeof(T));
      *(T*)buff.Data = val;
      return new(buff);
    }
  }

  public static Ref<T> Null { get; } = default;

  public static implicit operator Ref<T>(T val) => Box(val);
  public static bool operator ==(Ref<T> left, Ref<T> right) => left.Equals(right);
  public static bool operator !=(Ref<T> left, Ref<T> right) => !left.Equals(right);



  public delegate void Constructor<TVal>(ref TVal self);

  public struct RefCountedObject<TVal>
    where TVal : struct
  {
    public RefCountedObject(TVal val)
    {
      this.Value = val;
      this.refCount = 1;
    }

    public TVal Value;
    private volatile uint refCount;

    public uint RefCount => this.refCount;
    public void Init() => refCount = 1;
    public bool TryDisposeRemoveRef()
    {
      if (Interlocked.Decrement(ref this.refCount) == 0)
      {
        return true;
      }
      return false;
    }

    public void AddRef()
    {
      Interlocked.Increment(ref this.refCount);
    }
  }
}



public static class RefFactory
{
  public static Ref<TObj> New<TObj, TParams>(TParams parameters)
    where TObj : unmanaged, IObjectCtor<TParams>
  {
    var obj = Ref<TObj>.New();
    obj.Value.New(parameters);
    return obj;
  }
}