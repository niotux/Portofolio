using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using VB = Microsoft.VisualBasic;

namespace OpenUL.Manshell.Common;

public sealed class ExeCmd : ManshellCmd
{
  public override Type[] Args { get; } =
  [
    typeof(string),
    typeof(ManshellInfiniteArgs),
  ];
  public override int MinimumArgs => 1;
  public override string[] Names { get; } = ["exe", "."];

  protected override object? OnRun(ManshellContext context, ReadOnlySpan<ManshellArg> args)
  {
    var cmd = args[0].Value as string ?? throw new ManshellCmdArgException(this, 0, "Expected a Manshell command");
    Process? p = null;

    try
    {
      StringBuilder sb = new();
      for (int i = 1; i < args.Length; i++)
      {
        sb.Append('"');
        sb.Append(context.GetValue(args[i]));
        sb.Append('"');
        sb.Append(' ');
      }
      p = Process.Start(new ProcessStartInfo() { FileName = cmd, UseShellExecute = true, Arguments = sb.ToString() });
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
