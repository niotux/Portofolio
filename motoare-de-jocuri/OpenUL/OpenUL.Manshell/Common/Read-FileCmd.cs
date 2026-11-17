namespace OpenUL.Manshell.Common;

public sealed class Read_FileCmd : ManshellCmd
{
  public override Type[] Args { get; } =
  [
    typeof(string),
  ];
  public override string[] Names { get; } = ["read-file"];
  public override int MinimumArgs => 1;

  protected override object? OnRun(ManshellContext context, ReadOnlySpan<ManshellArg> args)
  {
    var path = context.GetValue(args[0]) as string ?? throw new InvalidOperationException();

    try
    {
      return File.ReadAllText(Directory.GetCurrentDirectory() + path);
    }
    catch (Exception ex)
    {
      context.Throw(ex);
      return null;
    }
  }
}
