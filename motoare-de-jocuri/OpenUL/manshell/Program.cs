using OpenUL.Manshell;
using OpenUL.Manshell.Common;

if (args.Length >= 1)
{
  Directory.SetCurrentDirectory(args[0]);
}

ManshellRuntime runtime = new();
runtime.Context.Assemblies.AddRange(AppDomain.CurrentDomain.GetAssemblies());
runtime.Context.AddCmds(ManshellLib.Common);
runtime.Context.AddCmds(ManshellLib.IO);
runtime.Context.AddCmds(ManshellLib.Runtime);

while (runtime.IsRunning)
{
  var command = runtime.Context.Console.ReadLine();
  runtime.Execute(command);
}