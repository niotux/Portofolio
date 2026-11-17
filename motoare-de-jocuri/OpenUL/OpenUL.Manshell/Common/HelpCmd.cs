using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using VB = Microsoft.VisualBasic;

namespace OpenUL.Manshell.Common;

public sealed class HelpCmd : ManshellCmd
{
  public override Type[] Args { get; } =
  [
    typeof(object),
  ];
  public override int MinimumArgs => 0;
  public override string[] Names { get; } = ["help", "?"];

  protected override object? OnRun(ManshellContext context, ReadOnlySpan<ManshellArg> args)
  {
    if (args.Length == 0)
    {
      foreach (var cmd in context.Cmds)
      {
        PrintHelp(context.Console, cmd);
      }
      return null;
    }

    var cmdsuper = context.GetValue(args[0]);
    var question = cmdsuper as ManshellCmd ?? throw new ManshellCmdArgException(this, 0, $"Expected a Manshell command, got {cmdsuper?.GetType().BaseType} instead");
    PrintHelp(context.Console, question);
    return null;
  }

  public static void PrintHelp(IManshellConsole console, ManshellCmd cmd)
  {
    for (uint i = 0; i < cmd.Names.Length; i++)
    {
      if (i > 0)
      {
        console.Foreground = ConsoleColor.Gray;
        console.StdOut.Write(" | ");
      }
      console.Foreground = ConsoleColor.DarkBlue;
      console.StdOut.Write(cmd.Names[i]);
    }
    uint a;
    for (a = 0; a < cmd.Args.Length; a++)
    {
      if (a == cmd.MinimumArgs + 1)
      {
        console.Foreground = ConsoleColor.Yellow;
        console.StdOut.Write(" [");
      }
      console.Foreground = ConsoleColor.DarkGray;
      console.StdOut.Write(" <");

      var arg = ManshellArg.GetArgDisplay(cmd.Args[a]);
      console.Foreground = arg.Special
        ? ConsoleColor.Magenta
        : ConsoleColor.DarkCyan;

      console.StdOut.Write(arg.Text);
      console.Foreground = ConsoleColor.DarkGray;
      console.StdOut.Write('>');
    }
    if (a > cmd.MinimumArgs + 1)
    {
      console.Foreground = ConsoleColor.Yellow;
      console.StdOut.Write(" ]");
    }
    console.ResetColor();
    console.StdOut.WriteLine();
  }
}