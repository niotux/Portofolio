using System.Reflection;

namespace OpenUL.Manshell.Common;

public sealed class Load_AssemblyCmd : ManshellCmd
{
  public override Type[] Args { get; } =
  [
    typeof(string),
  ];
  public override string[] Names { get; } = ["load-asm"];
  public override int MinimumArgs => 1;

  protected override object? OnRun(ManshellContext context, ReadOnlySpan<ManshellArg> args)
  {
    var path = context.GetValue(args[0]) as string ?? throw new InvalidOperationException();

    Assembly asm;
    try
    {
      asm = Assembly.LoadFrom(path);
    }
    catch (Exception ex)
    {
      context.Throw(ex);
      return null;
    }

    context.Assemblies.Add(asm);
    return asm;
  }
}
