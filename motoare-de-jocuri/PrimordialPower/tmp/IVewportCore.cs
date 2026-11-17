namespace PrimordialPower.Core;

public interface IViewportCore
{
  public ViewportNative Native { get; }

  
  string Title { get; set; }
  ConcurrentEvent.Action<string> OnTitleChanged { get; }


  (uint Width, uint Height) Size { get; set; }
  internal void SizeChanged(uint width, uint height);
  ConcurrentEvent.Action<uint, uint> OnSizeChanged { get; }


  (int X, int Y) Position { get; set; }
  internal void PositionChanged(int width, int height);
  ConcurrentEvent.Action<int, int> OnPositionChanged { get; }


  (int X, int Y) Offset { get; set; }
  internal void OffsetChanged(int width, int height);
  ConcurrentEvent.Action<int, int> OnOffsetChanged { get; }


  (float Min, float Max) Depth { get; set; }
  ConcurrentEvent.Action<float, float> OnDepthChanged { get; }


  ConcurrentEvent.Action OnInit { get; }


  bool ShouldClose { get; }
  bool IsClosed { get; }
  void CloseRequest();
  ConcurrentEvent.Func<bool> OnCloseRequested { get; }
  ConcurrentEvent.Action OnClose { get; }


  bool IsVisible { get; }
  void Show();
  void Hide();
  ConcurrentEvent.Action<bool> OnVisibilityChanged { get; }
}
