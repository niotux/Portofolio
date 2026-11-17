using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using VB = Microsoft.VisualBasic;

namespace OpenUL.Manshell.Common;

public sealed class CmdCmd : ManshellCmd
{
  public override Type[] Args { get; } =
  [
    typeof(object),
    typeof(ManshellInfiniteArgs),
  ];
  public override int MinimumArgs => 1;
  public override string[] Names { get; } = ["cmd", "!"];

  protected override object? OnRun(ManshellContext context, ReadOnlySpan<ManshellArg> args)
  {
    var cmd = context.GetValue(args[0]) as ManshellCmd ?? throw new ManshellCmdArgException(this, 0, "Expected a Manshell command");
    cmd.Run(context, args.Slice(1));

    return context.LastResult;
  }

}
