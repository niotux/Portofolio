namespace QuantiX.Engine.Rendering;

public interface ITexture : IGPUObject
{
  uint Width { get; }
  uint Height { get; }
  TextureFormat Format { get; }
  TextureMagFilter MagFilter { get; }
  TextureMinFilter MinFilter { get; }
  TextureWrap Wrapping { get; }
}
