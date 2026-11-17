namespace PrimordialPower.Platform;

public struct WindowInitConfig
{
  public WindowInitConfig() { }

  public required ViewportParams Params;
  public required Viewport Window;
  public SemaphoreSlim Semaphore = new(0, 1);
}
