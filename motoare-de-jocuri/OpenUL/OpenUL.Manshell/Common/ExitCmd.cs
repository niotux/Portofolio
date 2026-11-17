namespace OpenUL.Manshell.Common;

public sealed class ExitCmd : ManshellCmd
{
  public override Type[] Args { get; } =
  [
    typeof(int)
  ];
  public override string[] Names { get; } = ["exit"];
  public override int MinimumArgs => 0;

  protected override object? OnRun(ManshellContext context, ReadOnlySpan<ManshellArg> args)
  {
    context.IsRunning = false;
    return null;
  }
}