using System;
using QuantiX.Engine.Windowing;

namespace QuantiX.Engine.Rendering;

public interface IViewport : IGPUObject, IDrawableSurface
{
  public ISurface InnerSurface { get; }
  public void Present();
}
