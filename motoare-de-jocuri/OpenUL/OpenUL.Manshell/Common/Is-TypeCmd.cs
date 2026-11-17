namespace OpenUL.Manshell.Common;

public sealed class Is_TypeCmd : ManshellCmd
{
  public override Type[] Args { get; } =
  [
    typeof(object),
    typeof(Type),
  ];
  public override string[] Names { get; } = ["is-type"];
  public override int MinimumArgs => 1;

  protected override object? OnRun(ManshellContext context, ReadOnlySpan<ManshellArg> args)
  {
    var obj = context.GetValue(args[0]);
    var type = context.GetValue(args[1]) as Type ?? throw new ManshellCmdArgException(this, 0, "Expected a type");

    return type.IsInstanceOfType(obj);
  }
}
