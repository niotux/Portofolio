using QuantiX.Engine.Windowing;

namespace QuantiX.Engine.Rendering_IGNORE;

/// <summary>
/// The root factory and frame coordinator for all GPU resources.
/// One renderer per application; created directly by the backend (e.g. new OpenGLRenderer()).
/// </summary>
public interface IRenderer : IDisposable
{
  // -------------------------------------------------------------------------
  // Viewport — binds the renderer to an OS window
  // -------------------------------------------------------------------------

  IViewport CreateViewport(IWindow window);
  void DestroyViewport(IViewport viewport);

  // -------------------------------------------------------------------------
  // Shader — compile from source, opaque handle returned
  // -------------------------------------------------------------------------

  IShader CreateShader(ShaderDescriptor descriptor);
  void DestroyShader(IShader shader);

  // -------------------------------------------------------------------------
  // Texture — upload pixel data once, reference by handle
  // -------------------------------------------------------------------------

  IGpuTexture2D CreateTexture(uint width, uint height, TextureFormat format, ReadOnlySpan<byte> data);
  void DestroyTexture(IGpuTexture2D texture);

  // -------------------------------------------------------------------------
  // Pipeline — configured rendering unit; owns batched geometry
  // -------------------------------------------------------------------------

  IPipeline CreatePipeline(PipelineDesc desc);
  void DestroyPipeline(IPipeline pipeline);

  // -------------------------------------------------------------------------
  // Frame
  // -------------------------------------------------------------------------

  void Begin();
  void End();
}
