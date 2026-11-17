using QuantiX.Engine.Windowing;

namespace QuantiX.Engine.Rendering;

public interface IFramebuffer : IGPUObject, IDrawableSurface
{
  ITexture DepthTexture { get; }
  ITexture? ColorTexture { get; }
}
