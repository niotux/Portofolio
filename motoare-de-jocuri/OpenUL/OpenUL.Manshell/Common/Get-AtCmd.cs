namespace OpenUL.Manshell.Common;

public sealed class Get_AtCmd : ManshellCmd
{
  public override Type[] Args { get; } =
  [
    typeof(object),
    typeof(int),
    typeof(object),
  ];
  public override string[] Names { get; } = ["get-at", "get{}"];
  public override int MinimumArgs => 2;

  protected override object? OnRun(ManshellContext context, ReadOnlySpan<ManshellArg> args)
  {
    var self = args[0];
    object? obj = context.GetValue(self);

    if (obj == null)
    {
      context.Throw(new NullReferenceException());
      return null;
    }

    var indexArg = args[1];
    object? index = context.GetValue(indexArg);

    if (obj is Array array)
    {
      return array.GetValue(Convert.ToInt32(index));
    }

    var indexer = obj.GetType().GetProperties().FirstOrDefault(p => p.GetIndexParameters().Length > 0 && p.CanRead);
    if (indexer == null)
    {
      context.Throw(new MemberAccessException());
      return null;
    }

    var result = indexer.GetValue(obj, [index]);
    if (args.Length == 3)
    {
      context.SetVar(args[2].Name, result);
      return null;
    }
    return result;
  }
}
