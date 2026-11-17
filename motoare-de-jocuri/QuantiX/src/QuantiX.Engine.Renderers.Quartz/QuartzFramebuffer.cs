using GL = Silk.NET.OpenGL;
using QuantiX.Engine.Rendering;

namespace QuantiX.Engine.Renderers.Quartz;

internal sealed class QuartzFramebuffer : IFramebuffer
{
  private readonly GL.GL _gl;
  private readonly uint _fbo;
  private readonly uint _vao;
  private uint _width;
  private uint _height;

  public ITexture DepthTexture { get; }
  public ITexture? ColorTexture { get; }

  public GPUObjectHandle ApiHandle => new() { ApiHandleValue = _fbo };

  internal QuartzFramebuffer(GL.GL gl, uint width, uint height, TextureFormat depthFormat, TextureFormat? colorFormat)
  {
    _gl = gl;
    _width = width;
    _height = height;

    // Create depth texture with shadow comparison sampling
    DepthTexture = QuartzTexture.Create2D(gl, [], width, height, depthFormat,
      TextureMagFilter.Linear, TextureMinFilter.Linear, TextureWrap.ClampToBorder);

    uint depthHandle = (uint)DepthTexture.ApiHandle.ApiHandleValue;
    gl.BindTexture(GL.TextureTarget.Texture2D, depthHandle);
    float[] borderColor = [1f, 1f, 1f, 1f];
    unsafe
    {
      fixed (float* ptr = borderColor)
      {
        gl.TexParameter(GL.TextureTarget.Texture2D, GL.TextureParameterName.TextureBorderColor, ptr);
      }
    }
    gl.TexParameter(GL.TextureTarget.Texture2D, GL.TextureParameterName.TextureCompareMode,
      (int)GL.TextureCompareMode.CompareRefToTexture);
    gl.TexParameter(GL.TextureTarget.Texture2D, GL.TextureParameterName.TextureCompareFunc,
      (int)GL.DepthFunction.Lequal);
    gl.BindTexture(GL.TextureTarget.Texture2D, 0);

    // Optional color attachment
    if (colorFormat.HasValue)
    {
      ColorTexture = QuartzTexture.Create2D(gl, [], width, height, colorFormat.Value,
        TextureMagFilter.Linear, TextureMinFilter.Linear, TextureWrap.ClampToEdge);
    }

    // Create FBO
    _fbo = gl.GenFramebuffer();
    gl.BindFramebuffer(GL.FramebufferTarget.Framebuffer, _fbo);

    gl.FramebufferTexture2D(GL.FramebufferTarget.Framebuffer, GL.FramebufferAttachment.DepthAttachment,
      GL.TextureTarget.Texture2D, depthHandle, 0);

    if (ColorTexture != null)
    {
      uint colorHandle = (uint)ColorTexture.ApiHandle.ApiHandleValue;
      gl.FramebufferTexture2D(GL.FramebufferTarget.Framebuffer, GL.FramebufferAttachment.ColorAttachment0,
        GL.TextureTarget.Texture2D, colorHandle, 0);
    }
    else
    {
      gl.DrawBuffer(GL.DrawBufferMode.None);
      gl.ReadBuffer(GL.ReadBufferMode.None);
    }

    var status = gl.CheckFramebufferStatus(GL.FramebufferTarget.Framebuffer);
    gl.BindFramebuffer(GL.FramebufferTarget.Framebuffer, 0);

    if (status != GL.GLEnum.FramebufferComplete)
      throw new InvalidOperationException($"Framebuffer incomplete: {status}");

    _vao = gl.GenVertexArray();
  }

  public void Draw(Pipeline[] pipelines)
  {
    QuartzPipelineDrawer.Draw(_gl, _vao, _fbo, _width, _height, pipelines);
  }

  // ISurface — offscreen framebuffer: not visible, not interactive
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

  public void Dispose()
  {
    _gl.DeleteVertexArray(_vao);
    _gl.DeleteFramebuffer(_fbo);
    DepthTexture.Dispose();
    ColorTexture?.Dispose();
  }
}
