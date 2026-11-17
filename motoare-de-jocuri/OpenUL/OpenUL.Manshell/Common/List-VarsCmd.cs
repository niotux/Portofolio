namespace OpenUL.Manshell.Common;

public sealed class List_VarsCmd : ManshellCmd
{
  public override Type[] Args { get; } = [];
  public override string[] Names { get; } = ["list-vars"];
  public override int MinimumArgs => 0;

  protected override object? OnRun(ManshellContext context, ReadOnlySpan<ManshellArg> args)
  {
    foreach (var pair in context.Vars)
    {
      context.Console.StdOut.WriteLine($"{pair.Key} = {pair.Value}");
    }
    return null;
  }
}
