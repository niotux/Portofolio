using QuantiX.Engine.Windowing;

namespace QuantiX.Engine.RenderingTMP;

public interface IViewport
{
  Windowing.IWindow Surface { get; }
  uint Width { get; }
  uint Height { get; }
  void Present();
}
