namespace PrimordialPower.Platform;

public interface IViewport
{
  ViewportNative Native { get; }
  ViewportData Data { get; }



  bool IsClosed { get; }
  void Close();
  void Destroy();
  event Action? OnDestroy;
  event Action? OnClose;
  event Func<bool>? ConfirmClose;



  bool IsActive { get; }
  bool Updated { get; }



  string Title { get; set; }
  (uint Width, uint Height) Size { get; set; }
  (int X, int Y) Position { get; set; }
  (float Min, float Max) Depth { get; set; }
}