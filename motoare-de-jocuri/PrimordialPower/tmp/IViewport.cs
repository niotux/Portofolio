namespace PrimordialPower.Core;

public interface IViewport
{
  public ViewportNative Native { get; }


  string Title { get; set; }
  event Action<string>? OnTitleChanged;


  (uint Width, uint Height) Size { get; set; }
  void SizeChanged(uint width, uint height);
  event Action<uint, uint>? OnSizeChanged;


  (int X, int Y) Position { get; set; }
  void PositionChanged(int width, int height);
  event Action<int, int>? OnPositionChanged;


  (int X, int Y) Offset { get; set; }
  void OffsetChanged(int width, int height);
  event Action<int, int>? OnOffsetChanged;


  (float Min, float Max) Depth { get; set; }
  event Action<float, float>? OnDepthChanged;


  void Initialize(IViewportCore core);
  event Action? OnInit;


  bool ShouldClose { get; }
  bool IsClosed { get; }
  void CloseRequest();
  void CloseForce();
  event Func<bool>? OnCloseRequested;
  event Action? OnClose;


  bool IsVisible { get; }
  void Show();
  void Hide();
  event Action<bool>? OnVisibilityChanged;
}
