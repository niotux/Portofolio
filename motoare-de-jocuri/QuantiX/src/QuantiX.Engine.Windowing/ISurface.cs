using System;

namespace QuantiX.Engine.Windowing;

public interface ISurface
{
  (uint Width, uint Height) Size { get; set; }
  (int X, int Y) Position { get; set; }

  bool IsOpen { get; }
  bool IsVisible { get; }

  event Action<uint, uint>? OnSizeUpdate;
  event Action<int, int>? OnPositionUpdate;
  event Action<bool>? OnVisibilityUpdate;
  event Action? OnClose;
  event Action? OnDestroy;

  void SetSizePosition(uint width, uint height, int x, int y);
  void Open();
  Task OpenAsync();
  void Close();
  bool Update();
}
