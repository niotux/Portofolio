using GL = Silk.NET.OpenGL;
using QuantiX.Engine.Rendering;
using QuantiX.Engine.Windowing;

namespace QuantiX.Engine.Renderers.Quartz;

public sealed class QuartzRenderer : IRenderer
{
  private GL.GL? _gl;

  private GL.GL GetGL() => _gl ?? throw new InvalidOperationException("No viewport has been created yet.");

  public IViewport CreateViewport(IWindow window)
  {
    var viewport = new QuartzViewport(window);
    _gl ??= viewport.Gl;
    return viewport;
  }

  public IFramebuffer CreateFramebuffer(uint width, uint height, TextureFormat depthFormat, TextureFormat? colorFormat = null)
  {
    return new QuartzFramebuffer(GetGL(), width, height, depthFormat, colorFormat);
  }

  public IBuffer CreateBuffer(long size)
  {
    return new QuartzBuffer(GetGL(), size);
  }

  public ITexture CreateTexture1D(byte[] data, uint width, TextureFormat format, TextureMagFilter magFilter, TextureMinFilter minFilter, TextureWrap wrap)
  {
    return QuartzTexture.Create1D(GetGL(), data, width, format, magFilter, minFilter, wrap);
  }

  public ITexture CreateTexture2D(byte[] data, uint width, uint height, TextureFormat format, TextureMagFilter magFilter, TextureMinFilter minFilter, TextureWrap wrap)
  {
    return QuartzTexture.Create2D(GetGL(), data, width, height, format, magFilter, minFilter, wrap);
  }

  public ITexture CreateTexture2DArray(byte[] data, uint width, uint height, uint layers, TextureFormat format, TextureMagFilter magFilter, TextureMinFilter minFilter, TextureWrap wrap)
  {
    return QuartzTexture.Create2DArray(GetGL(), data, width, height, layers, format, magFilter, minFilter, wrap);
  }

  public ITexture CreateTexture3D(byte[] data, uint width, uint height, uint depth, TextureFormat format, TextureMagFilter magFilter, TextureMinFilter minFilter, TextureWrap wrap)
  {
    return QuartzTexture.Create3D(GetGL(), data, width, height, depth, format, magFilter, minFilter, wrap);
  }

  public ITexture CreateTextureCubeMap(byte[] face0, byte[] face1, byte[] face2, byte[] face3, byte[] face4, byte[] face5, uint width, uint height, TextureFormat format, TextureMagFilter magFilter, TextureMinFilter minFilter, TextureWrap wrap)
  {
    return QuartzTexture.CreateCubeMap(GetGL(), face0, face1, face2, face3, face4, face5, width, height, format, magFilter, minFilter, wrap);
  }

  public IShader CreateSpirVShader(ShaderProgram[] programs)
  {
    return new QuartzShader(GetGL(), programs);
  }

  public void Dispose() { }
}
