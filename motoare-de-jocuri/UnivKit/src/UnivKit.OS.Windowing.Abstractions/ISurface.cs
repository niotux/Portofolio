using System;

namespace UnivKit.OS.Windowing;

public interface ISurface
{
  (uint Width, uint Height) Size { get; }

  SurfaceHandle? Handle { get; }

  bool IsVisible { get; }

  event Action<uint, uint>? OnSizeChanged;
  event Action<bool>? OnVisibilityChanged;
}
