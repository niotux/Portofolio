using System.Numerics;

namespace OpenUL.Manshell.Common;

public sealed class Set_AtCmd : ManshellCmd
{
  public override Type[] Args { get; } =
  [
    typeof(object),
    typeof(int),
    typeof(object),
  ];
  public override string[] Names { get; } = ["set-at", "set{}"];
  public override int MinimumArgs => 3;

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

    var valueArg = args[2];
    object? value = context.GetValue(valueArg);


    if (obj is Array array)
    {
      array.SetValue(value, Convert.ToInt32(index));
      return null;
    }

    var indexer = obj.GetType().GetProperties().FirstOrDefault(p => p.GetIndexParameters().Length > 0 && p.CanWrite);
    if (indexer == null)
    {
      context.Throw(new MemberAccessException());
      return null;
    }

    indexer.SetValue(obj, value, [index]);
    return null;
  }
}
