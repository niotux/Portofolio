namespace OpenUL.Manshell.Common;

public sealed class Write_FileCmd : ManshellCmd
{
  public override Type[] Args { get; } =
  [
    typeof(string),
    typeof(object),
  ];
  public override string[] Names { get; } = ["write-file"];
  public override int MinimumArgs => 2;

  protected override object? OnRun(ManshellContext context, ReadOnlySpan<ManshellArg> args)
  {
    var path = args[0].Value as string ?? throw new InvalidOperationException();
    var contentArg = args[1];
    object? content = context.GetValue(contentArg);


    try
    {
      File.WriteAllText(Directory.GetCurrentDirectory() + path, content?.ToString() ?? "");
    }
    catch (Exception ex)
    {
      context.Throw(ex);
    }

    return null;
  }
}
