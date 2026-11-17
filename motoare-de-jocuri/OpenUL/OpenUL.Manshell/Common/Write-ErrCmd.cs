namespace OpenUL.Manshell.Common;

public sealed class Write_ErrCmd : ManshellCmd
{
  public override Type[] Args { get; } =
  [
    typeof(object),
  ];
  public override string[] Names { get; } = ["write-err"];
  public override int MinimumArgs => 1;

  protected override object? OnRun(ManshellContext context, ReadOnlySpan<ManshellArg> args)
  {
    var valueArg = args[0];
    object? value = context.GetValue(valueArg);


    context.Console.StdErr.WriteLine(value);
    return null;
  }
}
