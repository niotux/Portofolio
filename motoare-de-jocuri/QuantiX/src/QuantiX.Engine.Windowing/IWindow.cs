namespace QuantiX.Engine.Windowing;

public interface IWindow : ISurface
{
  string Title { get; set; }
  WindowHandle Handle { get; }

  bool IsFocused { get; }

  (bool X, bool Y) IsDefaultPosition { get; }

  event Action<bool>? OnFocusUpdate;
  event Func<bool>? OnConfirmClose;

  void Focus();
}

public interface IWindow<TSelf> : IWindow
where TSelf : IWindow<TSelf>
{
  IWindowManager<TSelf>? Manager { get; set; }
  static abstract TSelf Create(string title, uint width, uint height, int? x = null, int? y = null);
}