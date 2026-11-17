namespace OpenUL.Manshell.Common;

public sealed class Get_TypeCmd : ManshellCmd
{
  public override Type[] Args { get; } =
  [
    typeof(object),
  ];
  public override string[] Names { get; } = ["get-type"];
  public override int MinimumArgs => 1;

  protected override object? OnRun(ManshellContext context, ReadOnlySpan<ManshellArg> args)
  {
    var self = args[0];
    object? obj = context.GetValue(self);


    return obj?.GetType();
  }
}
