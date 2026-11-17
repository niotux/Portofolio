using QuantiX.Engine.Rendering;
using QuantiX.Engine.Windowing;

namespace QuantiX.Engine.Renderers.Glyph;

internal sealed class GlyphFramebuffer : IFramebuffer
{
  private static ulong _nextHandle = 1;

  private readonly ulong _handle;
  private readonly GlyphRasterizer _rasterizer;
  private uint _width;
  private uint _height;

  public ITexture DepthTexture { get; }
  public ITexture? ColorTexture { get; }

  public GPUObjectHandle ApiHandle => new() { ApiHandleValue = _handle };

  internal GlyphFramebuffer(uint width, uint height, TextureFormat depthFormat, TextureFormat? colorFormat)
  {
    _handle = _nextHandle++;
    _width = width;
    _height = height;

    DepthTexture = new GlyphTexture([], width, height, depthFormat,
      TextureMagFilter.Linear, TextureMinFilter.Linear, TextureWrap.ClampToEdge);

    if (colorFormat.HasValue)
    {
      ColorTexture = new GlyphTexture([], width, height, colorFormat.Value,
        TextureMagFilter.Linear, TextureMinFilter.Linear, TextureWrap.ClampToEdge);
    }

    _rasterizer = new GlyphRasterizer(width, height);
  }

  internal GlyphRasterizer Rasterizer => _rasterizer;

  public void Draw(Pipeline[] pipelines)
  {
    _rasterizer.Resize(_width, _height);
    _rasterizer.Clear();
    _rasterizer.Rasterize(pipelines);
  }

  // ISurface — offscreen framebuffer
  public (uint Width, uint Height) Size
  {
    get => (_width, _height);
    set { _width = value.Width; _height = value.Height; }
  }
  public (int X, int Y) Position { get => (0, 0); set { } }
  public bool IsOpen => true;
  public bool IsVisible => false;
  public event Action<uint, uint>? OnSizeUpdate;
  public event Action<int, int>? OnPositionUpdate;
  public event Action<bool>? OnVisibilityUpdate;
  public event Action? OnClose;
  public event Action? OnDestroy;
  public void SetSizePosition(uint width, uint height, int x, int y) { _width = width; _height = height; }
  public void Open() { }
  public Task OpenAsync() => Task.CompletedTask;
  public void Close() { }
  public bool Update() => true;

  public void Dispose() { }
}
