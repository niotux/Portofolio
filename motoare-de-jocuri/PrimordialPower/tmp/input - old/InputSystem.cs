namespace PrimordialPower.Core;

public class InputSystem : IInputSystem
{
  private IInputSystem _NativeSystem;
  public InputSystem()
  {
    if (OperatingSystem.IsWindows())
    {
      _NativeSystem = new Win32.Win32InputSystem();
    }
    else
      throw new Exception("What kind of obscure OS is this? You think I care? Too bad.");
  }



  public InputContext CreateInputContext(IViewport viewportContext)
  {
    return _NativeSystem.CreateInputContext(viewportContext);
  }



  public Task Run(CancellationToken cancellationToken)
  {
    return _NativeSystem.Run(cancellationToken);
  }

  public Task SignalShutdown()
  {
    return _NativeSystem.SignalShutdown();
  } 
}
