using PrimordialPower.Platform;
using PrimordialPower.Runtime;

namespace DemoPrime;

public class MainServer : AppServer
{
  public override void Init()
  {
    AppGlobal.Viewport.Open();
    AppGlobal.AudioLib.MainTheme.Play();
  }

  public override void Run()
  {
    while (AppGlobal.Viewport.IsOpen)
    {

      AppGlobal.Viewport.Update();
    }
  }

  public override void OnStop()
  {
    AppGlobal.Viewport.Close();
  }

  public override void Shutdown()
  {
    AppGlobal.AudioLib.MainTheme.Dispose();
  }
}
