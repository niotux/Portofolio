using QuantiX.Engine.Rendering;

namespace QuantiX.Engine.Renderers.Glyph;

internal sealed class GlyphTexture : ITexture
{
  private static ulong _nextHandle = 1;

  private readonly ulong _handle;

  internal byte[] Data { get; }

  public uint Width { get; }
  public uint Height { get; }
  public TextureFormat Format { get; }
  public TextureMagFilter MagFilter { get; }
  public TextureMinFilter MinFilter { get; }
  public TextureWrap Wrapping { get; }

  public GPUObjectHandle ApiHandle => new() { ApiHandleValue = _handle };

  internal GlyphTexture(byte[] data, uint width, uint height, TextureFormat format,
    TextureMagFilter magFilter, TextureMinFilter minFilter, TextureWrap wrap)
  {
    _handle = _nextHandle++;
    Data = data;
    Width = width;
    Height = height;
    Format = format;
    MagFilter = magFilter;
    MinFilter = minFilter;
    Wrapping = wrap;
  }

  public void Dispose() { }
}
