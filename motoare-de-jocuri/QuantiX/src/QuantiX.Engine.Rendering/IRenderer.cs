using QuantiX.Engine.Windowing;

namespace QuantiX.Engine.Rendering;

public interface IRenderer : IDisposable
{
  ITexture CreateTexture1D(byte[] data, uint width, TextureFormat format, TextureMagFilter magFilter, TextureMinFilter minFilter, TextureWrap wrap);
  ITexture CreateTexture2D(byte[] data, uint width, uint height, TextureFormat format, TextureMagFilter magFilter, TextureMinFilter minFilter, TextureWrap wrap);
  ITexture CreateTexture2DArray(byte[] data, uint width, uint height, uint layers, TextureFormat format, TextureMagFilter magFilter, TextureMinFilter minFilter, TextureWrap wrap);
  ITexture CreateTexture3D(byte[] data, uint width, uint height, uint depth, TextureFormat format, TextureMagFilter magFilter, TextureMinFilter minFilter, TextureWrap wrap);
  ITexture CreateTextureCubeMap(byte[] face0, byte[] face1, byte[] face2, byte[] face3, byte[] face4, byte[] face5, uint width, uint height, TextureFormat format, TextureMagFilter magFilter, TextureMinFilter minFilter, TextureWrap wrap);
  IShader CreateSpirVShader(ShaderProgram[] programs);
  IViewport CreateViewport(IWindow window);
  IBuffer CreateBuffer(long size);
  IFramebuffer CreateFramebuffer(uint width, uint height, TextureFormat depthFormat, TextureFormat? colorFormat = null);
}
