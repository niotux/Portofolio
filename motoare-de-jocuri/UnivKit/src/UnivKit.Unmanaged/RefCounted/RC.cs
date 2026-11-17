using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace UnivKit.Unmanaged.RefCounted;

[StructLayout(LayoutKind.Sequential)]
public struct RC<T> : IRefCounted<RC<T>>
  where T : unmanaged
{
  private RCBuffer buffer;

  public RC()
  {
    this = New(null);
  }

  public RC(Constructor<T> ctor)
  {
    this = New(ctor);
  }

  public RC(T val)
  {
    this = Box(val);
  }

  public RC(RCBuffer buffer)
  {
    this.buffer = buffer;
  }

  public readonly unsafe ref T Value => ref *(T*)this.buffer.Data;
  public readonly long RefCount => this.buffer.RefCount;

  public static RC<T> New(Constructor<T>? ctor = null)
  {
    unsafe
    {
      RCBuffer obj = new((long)sizeof(T));
      ctor?.Invoke(ref *(T*)obj.Data);
      return new(obj);
    }
  }

  public readonly RC<T> Clone()
  {
    unsafe
    {
      RCBuffer buff = new((long)sizeof(T));
      Unsafe.CopyBlock(buff.Data, this.buffer.Data, (uint)sizeof(T));
      return new(buff);
    }
  }


  public readonly bool Equals(RC<T> other) => this.buffer.Equals(other.buffer);
  public readonly override bool Equals(object? obj) => this.buffer.Equals(obj);
  public readonly override int GetHashCode() => this.buffer.GetHashCode();
  public readonly override string? ToString() => this.Value.ToString();

  public static RC<T> Box(T val)
  {
    unsafe
    {
      RCBuffer buff = new((long)sizeof(T));
      *(T*)buff.Data = val;
      return new(buff);
    }
  }


  public static implicit operator RC<T>(T val) => Box(val);
  public static bool operator ==(RC<T> left, RC<T> right) => left.Equals(right);
  public static bool operator !=(RC<T> left, RC<T> right) => !left.Equals(right);
  public static RC<T> operator ++(RC<T> o)
  {
    o.buffer++;
    return o;
  }
  public static RC<T> operator --(RC<T> o)
  {
    o.buffer--;
    return o;
  }

  public void Dispose()
  {
    if (this.RefCount == 1 && this.Value is IDisposable disposable)
    {
      disposable.Dispose();
    }
    this--;
  }


  public delegate void Constructor<TVal>(ref TVal self);
}