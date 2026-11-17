using System;
using System.Runtime.CompilerServices;
using PrimordialPower.Runtime;

namespace PrimordialPower.Tools;

public class ConsoleInspectorServer : Server<IInspectorServices>
{
  private Inspector inspector = new();
  public override ServerType Type => ServerType.Async;
  public override IInspectorServices Services => this.inspector;


  private const string DirectScriptFunction = "|  ";
  private string prompt = DirectScriptFunction;

  private bool loop = Launcher.ContinueLoop;
  public override async Task<bool> LoopAsync()
  {
    Console.ResetColor();
    Console.ForegroundColor = ConsoleColor.DarkBlue;
    Console.Write(prompt);
    Console.ForegroundColor = ConsoleColor.DarkGray;
    try
    {
      string? line = Console.ReadLine();
      Console.ResetColor();

      var result = await this.inspector.InterpretLine(line);
      if (result.State == Inspector.InterpretResultState.ExecutedScript)
      {
        Console.Write("\n");
      }
    }
    catch (Exception ex)
    {
      Console.ForegroundColor = ConsoleColor.Red;
      Console.WriteLine(ex.Message);
      Console.ResetColor();
    }
    finally
    {
      Console.ResetColor();
    }
    return this.loop;
  }

  public override Task ShutdownAsync()
  {
    Console.ResetColor();
    return Task.CompletedTask;
  }

  public override void SignalShutdown()
  {
    this.loop = Launcher.StopLoop;
  }
}
