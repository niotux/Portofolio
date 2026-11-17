using System.Runtime.InteropServices;
using GL = Silk.NET.OpenGL;
using QuantiX.Engine.Rendering;

namespace QuantiX.Engine.Renderers.Quartz;

internal sealed class QuartzBuffer : IBuffer
{
  private readonly GL.GL _gl;
  private readonly uint _handle;
  private readonly long _size;

  public QuartzBuffer(GL.GL gl, long size)
  {
    _gl = gl;
    _size = size;
    _handle = _gl.GenBuffer();
    _gl.BindBuffer(GL.BufferTargetARB.ArrayBuffer, _handle);
    unsafe
    {
      _gl.BufferData(GL.BufferTargetARB.ArrayBuffer, (nuint)size, null, GL.BufferUsageARB.DynamicDraw);
    }
    _gl.BindBuffer(GL.BufferTargetARB.ArrayBuffer, 0);
  }

  public GPUObjectHandle ApiHandle => new() { ApiHandleValue = _handle };

  public long Length => _size;

  public void Write<T>(ReadOnlySpan<T> data, uint byteOffset) where T : unmanaged
  {
    ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(data);
    _gl.BindBuffer(GL.BufferTargetARB.ArrayBuffer, _handle);
    unsafe
    {
      fixed (byte* ptr = bytes)
      {
        _gl.BufferSubData(GL.BufferTargetARB.ArrayBuffer, (nint)byteOffset, (nuint)bytes.Length, ptr);
      }
    }
    _gl.BindBuffer(GL.BufferTargetARB.ArrayBuffer, 0);
  }

  public void Write<T>(T data, uint byteOffset) where T : unmanaged
  {
    ReadOnlySpan<T> span = new(ref data);
    Write(span, byteOffset);
  }

  public void Clear()
  {
    _gl.BindBuffer(GL.BufferTargetARB.ArrayBuffer, _handle);
    unsafe
    {
      _gl.BufferData(GL.BufferTargetARB.ArrayBuffer, (nuint)_size, null, GL.BufferUsageARB.DynamicDraw);
    }
    _gl.BindBuffer(GL.BufferTargetARB.ArrayBuffer, 0);
  }

  public void Dispose()
  {
    _gl.DeleteBuffer(_handle);
  }
}
