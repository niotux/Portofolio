using QuantiX.Engine.Windowing;

namespace QuantiX.Engine.RenderingTMP;

public interface IRenderer
{
  // --- Shader compilation ---
  IShader CompileShader(string vertexSource, string fragmentSource);

  // --- Texture creation ---
  IGpuTexture CreateTexture(uint width, uint height, TextureFormat format, ReadOnlySpan<byte> data);

  // --- Viewport management ---
  IViewport CreateViewport(Windowing.IWindow surface);
  void DestroyViewport(IViewport viewport);

  // --- Module management ---
  IRenderableModule CreateModule(RenderableModuleDescriptor descriptor);
  void DestroyModule(IRenderableModule module);

  // --- Frame lifecycle ---
  void BeginFrame();
  void EndFrame();
}
