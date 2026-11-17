namespace OpenUL.Manshell.Common;

public sealed class Get_BaseCmd : ManshellCmd
{
  public override Type[] Args { get; } =
  [
    typeof(object),
  ];
  public override string[] Names { get; } = ["get-base", ":"];
  public override int MinimumArgs => 1;

  protected override object? OnRun(ManshellContext context, ReadOnlySpan<ManshellArg> args)
  {
    var obj = context.GetValue(args[0]);
    var type = obj as Type ?? obj?.GetType() ?? throw new NullReferenceException("Expected an object or type");
    return type.BaseType;
  }
}
