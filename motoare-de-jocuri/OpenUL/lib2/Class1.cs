using OpenUL.Manshell;

namespace lib2;

public class TestNCmd : ManshellCmd
{
  public override Type[] Args { get; } =
  [
    typeof(string),
    typeof(int),
  ];

  public override string[] Names => ["test-n"];

  public override int MinimumArgs => 2;

  protected override object? OnRun(ManshellContext context, ReadOnlySpan<ManshellArg> args)
  {
    var text = context.GetValue(args[0]) as string;
    var repeat = (int)(context.GetValue(args[1]) ?? 0);
    for (int i = 0; i < repeat; i++)
    {
      context.Console.StdOut.WriteLine(text);
    }
    return null;
  }
}
