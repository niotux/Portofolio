using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using VB = Microsoft.VisualBasic;

namespace OpenUL.Manshell.Common;

public sealed class WhileCmd : ManshellCmd
{
  public override Type[] Args { get; } =
  [
    typeof(bool),
    typeof(ManshellCmd),
    typeof(ManshellInfiniteArgs),
  ];
  public override int MinimumArgs => 2;
  public override string[] Names { get; } = ["while"];

  protected override object? OnRun(ManshellContext context, ReadOnlySpan<ManshellArg> args)
  {
    var condition = (bool)(args[0].Value ?? throw new InvalidOperationException("Expected a System.Boolean"));
    var cmd = args[1].Value as ManshellCmd ?? throw new InvalidOperationException("Expected a System.Boolean");
    while (condition)
    {
      cmd.Run(context, args.Slice(2));
    }
    return context.LastResult;
  }

}
