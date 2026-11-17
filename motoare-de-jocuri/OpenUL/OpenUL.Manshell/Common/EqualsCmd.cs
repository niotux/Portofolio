namespace OpenUL.Manshell.Common;

public sealed class EqualsCmd : ManshellCmd
{
  public override Type[] Args { get; } =
  [
    typeof(object),
    typeof(object),
  ];
  public override string[] Names { get; } = ["equals", "=="];
  public override int MinimumArgs => 1;

  protected override object? OnRun(ManshellContext context, ReadOnlySpan<ManshellArg> args)
  {
    var obj = context.GetValue(args[0]);
    var type = context.GetValue(args[1]);

    return obj?.Equals(type) ?? type == null;
  }
}
