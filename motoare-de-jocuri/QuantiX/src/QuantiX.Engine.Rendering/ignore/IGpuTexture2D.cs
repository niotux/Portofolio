namespace QuantiX.Engine.Rendering_IGNORE;

/// <summary>
/// An opaque handle to a texture residing on the GPU.
/// Created by IRenderer.CreateTexture — do not instantiate directly.
/// </summary>
public interface IGpuTexture2D : IDisposable
{
  uint Width { get; }
  uint Height { get; }
  TextureFormat Format { get; }
}
