namespace OpenUL.Manshell.Common;

public sealed class ClearCmd : ManshellCmd
{
  public override Type[] Args { get; } = [];
  public override string[] Names { get; } = ["clear", "cls"];
  public override int MinimumArgs => 0;

  protected override object? OnRun(ManshellContext context, ReadOnlySpan<ManshellArg> args)
  {
    context.Console.Clear();
    return null;
  }
}
