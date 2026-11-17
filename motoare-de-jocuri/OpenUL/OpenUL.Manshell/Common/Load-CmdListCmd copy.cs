using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using VB = Microsoft.VisualBasic;

namespace OpenUL.Manshell.Common;

public sealed class Load_CmdListCmd : ManshellCmd
{
  public override Type[] Args { get; } =
  [
    typeof(IEnumerable<object>),
  ];
  public override int MinimumArgs => 1;
  public override string[] Names { get; } = ["load-cmdlist", "load-cmd{}"];

  protected override object? OnRun(ManshellContext context, ReadOnlySpan<ManshellArg> args)
  {
    var cmd = context.GetValue(args[0]) as IEnumerable<ManshellCmd> ?? throw new InvalidOperationException("Expected a Manshell command");
    context.AddCmds(cmd);
    return context.LastResult;
  }

}
