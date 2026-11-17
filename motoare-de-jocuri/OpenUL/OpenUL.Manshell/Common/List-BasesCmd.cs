namespace OpenUL.Manshell.Common;

public sealed class List_BasesCmd : ManshellCmd
{
  public override Type[] Args { get; } =
  [
    typeof(object),
  ];
  public override string[] Names { get; } = ["list-bases", "::"];
  public override int MinimumArgs => 1;

  protected override object? OnRun(ManshellContext context, ReadOnlySpan<ManshellArg> args)
  {
    var obj = context.GetValue(args[0]);
    var type = obj as Type ?? obj?.GetType() ?? throw new NullReferenceException("Expected an object or type");
    for (Type? inner = type.BaseType; inner != null; inner = inner.BaseType)
    {
      context.Console.PrintObject(inner);
      context.Console.StdOut.WriteLine();
    }
    return null;
  }
}
