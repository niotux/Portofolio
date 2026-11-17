using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using VB = Microsoft.VisualBasic;

namespace OpenUL.Manshell.Common;

public sealed class RunCmd : ManshellCmd
{
  public override Type[] Args { get; } =
  [
    typeof(string),
    typeof(object[]),
  ];
  public override int MinimumArgs => 1;
  public override string[] Names { get; } = ["run"];

  protected override object? OnRun(ManshellContext context, ReadOnlySpan<ManshellArg> args)
  {
    var cmd = args[0].Value as string ?? throw new InvalidOperationException("Expected a Manshell command");
    var pargs = args.Length > 1
      ? args[1].Value as object[]
      : null;
    Process? p = null;

    try
    {
      if (pargs == null)
      {
        p = Process.Start(new ProcessStartInfo(cmd));
      }
      else
      {
        p = Process.Start(new ProcessStartInfo(cmd, pargs.Select((o) => o.ToString() ?? string.Empty)));
      }
      p?.WaitForExit();
    }
    catch (Exception ex)
    {
      context.Throw(ex);
    }

    if (p == null)
    {
      return null;
    }
    var ec = p.ExitCode;
    p.Dispose();
    return ec;
  }

}
