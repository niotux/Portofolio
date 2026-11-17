using System.Runtime.InteropServices;
using QuantiX.Engine.Rendering;

namespace QuantiX.Engine.Renderers.Glyph;

internal sealed class GlyphBuffer : IBuffer
{
  private static ulong _nextHandle = 1;

  private readonly ulong _handle;
  private byte[] _data;

  public GlyphBuffer(long size)
  {
    _handle = _nextHandle++;
    _data = new byte[size];
  }

  public GPUObjectHandle ApiHandle => new() { ApiHandleValue = _handle };
  public long Length => _data.Length;

  internal ReadOnlySpan<byte> Data => _data;

  public void Write<T>(ReadOnlySpan<T> data, uint byteOffset) where T : unmanaged
  {
    ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(data);
    bytes.CopyTo(_data.AsSpan((int)byteOffset));
  }

  public void Write<T>(T data, uint byteOffset) where T : unmanaged
  {
    ReadOnlySpan<T> span = new(ref data);
    Write(span, byteOffset);
  }

  public void Clear()
  {
    Array.Clear(_data, 0, _data.Length);
  }

  public void Dispose() { }
}
