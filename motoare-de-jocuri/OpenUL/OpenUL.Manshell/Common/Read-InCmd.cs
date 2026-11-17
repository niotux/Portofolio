namespace OpenUL.Manshell.Common;

public sealed class Read_InCmd : ManshellCmd
{
  public override Type[] Args { get; } =
  [
    typeof(ManshellNamedArg),
  ];
  public override string[] Names { get; } = ["read-in", "read"];
  public override int MinimumArgs => 0;

  protected override object? OnRun(ManshellContext context, ReadOnlySpan<ManshellArg> args)
  {
    var line = context.Console.StdIn.ReadLine();

    if (args.Length > 0)
    {
      context.SetVar(args[0].Name, line);
      return null;
    }

    return line;
  }
}
