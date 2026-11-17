using System.Resources;
using System.Runtime.InteropServices;

namespace OpenUL.Manshell;

public class ManshellRuntime
{
  public ManshellContext Context { get; }
  public bool IsRunning => Context.IsRunning;

  public ManshellRuntime() : this(new ManshellContext()) { }
  public ManshellRuntime(ManshellContext ctx)
  {
    Context = ctx;
  }

  public void Execute(string? line)
  {
    var commands = Parser.ParseLine(Context, line);
    for (int i = 0; i < commands.Count; i++)
    {
      if (commands[i].Count == 0)
      {
        continue;
      }

      if (commands[i][0].Value is ManshellCmd cmd)
      {
        cmd.Run(Context, CollectionsMarshal.AsSpan(commands[i]).Slice(1));
      }
      else if (commands[i].Count == 1)
      {
        Context.LastResult = commands[i][0].Value;
      }
      else
      {
        var array = new object[commands[i].Count];
        for (int argIdx = 0; argIdx < array.Length; argIdx++)
        {
          array[argIdx] = commands[i][argIdx];
        }
        Context.LastResult = array;
      }

      if (Context.LastException != null)
      {
        Context.Console.PrintException(Context.LastException);
        continue;
      }
    }
    if (Context.LastException == null && Context.LastResult != null)
    {
      Context.Console.PrintResult(Context.LastResult);
    }
    Context.ClearException();
  }
}