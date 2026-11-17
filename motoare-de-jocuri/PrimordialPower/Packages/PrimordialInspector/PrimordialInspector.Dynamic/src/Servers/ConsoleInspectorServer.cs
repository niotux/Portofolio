using PrimordialPower.Core;


namespace PrimordialInspector.Scripting.Servers;

public class ConsoleInspectorServer : TaskServer<IInspectorServices>
{
  private readonly RuntimeScriptLoader _loader = new();
  private readonly InspectorServices _services;

  public override IInspectorServices Services => _services;

  public ConsoleInspectorServer()
  {
    _services = new InspectorServices(this);
  }



  private bool loop = Launcher.ContinueLoop;
  public override async Task<bool> Loop()
  {
    var code = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(code))
      return this.loop;

    var (instance, usings) = _services.GetFocus();
    var script = _loader.LoadScript(instance, code, usings);
    await script();

    return this.loop;
  }



  public override void SignalShutdown()
  {
    this.loop = Launcher.StopLoop;
  }
}
