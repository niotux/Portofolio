using OpenUL.Manshell;
using OpenUL.Manshell.Common;

namespace Game;

public static class Shell
{
  public static void Run((string Name, object? Value)[]? variables = null)
  {
    ManshellRuntime runtime = new();
    runtime.Context.Assemblies.AddRange(AppDomain.CurrentDomain.GetAssemblies());
    runtime.Context.AddCmds(ManshellLib.Common);
    runtime.Context.AddCmds(ManshellLib.IO);
    runtime.Context.AddCmds(ManshellLib.Runtime);
    if (variables != null)
    {
      foreach (var var in variables)
      {
        runtime.Context.SetVar(var.Name, var.Value);
      }
    }

    while (runtime.IsRunning)
    {
      var command = runtime.Context.Console.ReadLine();
      runtime.Execute(command);
    }
  }
}