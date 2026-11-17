namespace OpenUL.Manshell.Common;

public sealed class Set_DirCmd : ManshellCmd
{
  public override Type[] Args { get; } =
  [
    typeof(string)
  ];
  public override string[] Names { get; } = ["set-dir", "cd"];
  public override int MinimumArgs => 1;

  protected override object? OnRun(ManshellContext context, ReadOnlySpan<ManshellArg> args)
  {
    Directory.SetCurrentDirectory(context.GetValue(args[0]) as string ?? string.Empty);
    return null;
  }
}
