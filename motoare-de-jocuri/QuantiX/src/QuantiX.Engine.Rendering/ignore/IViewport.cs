using QuantiX.Engine.Windowing;

namespace QuantiX.Engine.Rendering_IGNORE;

/// <summary>
/// A render target bound to a specific OS window.
/// Created by IRenderer.CreateViewport — do not instantiate directly.
/// </summary>
public interface IViewport : IDisposable
{
  IWindow Window { get; }
  uint Width { get; }
  uint Height { get; }

  void Present();
}
