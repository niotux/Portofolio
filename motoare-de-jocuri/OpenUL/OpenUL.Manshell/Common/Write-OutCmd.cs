namespace OpenUL.Manshell.Common;

public sealed class Write_OutCmd : ManshellCmd
{
  public override Type[] Args { get; } =
  [
    typeof(object),
  ];
  public override string[] Names { get; } = ["write-out", "write"];
  public override int MinimumArgs => 1;

  protected override object? OnRun(ManshellContext context, ReadOnlySpan<ManshellArg> args)
  {
    var valueArg = args[0];
    object? value = context.GetValue(valueArg);


    context.Console.StdOut.WriteLine(value);
    return null;
  }
}
