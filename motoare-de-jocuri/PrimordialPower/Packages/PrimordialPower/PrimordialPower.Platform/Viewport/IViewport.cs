namespace PrimordialPower.Platform;

public interface IViewport
{
  string Title { get; set; }
  (uint Width, uint Height) Size { get; set; }
  (int X, int Y) Position { get; set; }
  (int X, int Y) Offset { get; set; }
  ViewportHandle Handle { get; }

  bool IsOpen { get; }
  bool IsVisible { get; }
  bool IsFocused { get; }

  (bool X, bool Y) IsDefaultPosition { get; }

  event Action<uint, uint>? OnSizeUpdate;
  event Action<int, int>? OnPositionUpdate;
  event Action<int, int>? OnOffsetUpdate;
  event Action<bool>? OnVisibilityUpdate;
  event Action<bool>? OnFocusUpdate;
  event Action? OnClose;
  event Func<bool>? OnConfirmClose;
  event Action? OnDestroy;

  void SetSizePosition(uint width, uint height, int x, int y);
  void Focus();
  void Open();
  Task OpenAsync();
  void Close();
  bool Update();
}

public interface IViewport<TSelf> : IViewport
where TSelf : IViewport<TSelf>
{
  IViewportManager<TSelf>? Manager { get; set; }
  static abstract TSelf Create(string title, uint width, uint height, int? x = null, int? y = null, int xOffset = 0, int yOffset = 0);
}