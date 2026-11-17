namespace OpenUL.Manshell.Common;

public sealed class Get_DirCmd : ManshellCmd
{
  public override Type[] Args { get; } =
  [
    typeof(string)
  ];
  public override string[] Names { get; } = ["get-dir"];
  public override int MinimumArgs => 0;

  protected override object? OnRun(ManshellContext context, ReadOnlySpan<ManshellArg> args)
  {
    if (args.Length == 0)
    {
      return Directory.GetCurrentDirectory();
    }

    if (args.Length == 1)
    {
      return Directory.GetFileSystemEntries(Directory.GetCurrentDirectory(), context.GetValue(args[0]) as string ?? string.Empty);
    }
    return null;
  }
}
