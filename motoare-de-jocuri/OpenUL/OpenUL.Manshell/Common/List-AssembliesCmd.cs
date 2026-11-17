using System.Reflection;
using System.Runtime.InteropServices;
using VB = Microsoft.VisualBasic;

namespace OpenUL.Manshell.Common;

public sealed class List_AssembliesCmd : ManshellCmd
{
  public override Type[] Args { get; } = [];
  public override string[] Names { get; } = ["list-asm"];
  public override int MinimumArgs => 0;

  protected override object? OnRun(ManshellContext context, ReadOnlySpan<ManshellArg> args)
  {
    return context.Assemblies;
  }
}
