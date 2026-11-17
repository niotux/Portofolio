namespace PrimordialPower.Platform;

public struct Mouse
{
  public Mouse() { }


  private volatile bool _LeftButton = false;
  public bool LeftButton
  {
    get => _LeftButton;
    internal set => _LeftButton = value;
  }


  private volatile bool _RightButton = false;
  public bool RightButton
  {
    get => _RightButton;
    internal set => _RightButton = value;
  }


  private volatile bool _XScrollDelta = false;
  public bool XScrollDelta
  {
    get => _XScrollDelta;
    internal set => _XScrollDelta = value;
  }
}
