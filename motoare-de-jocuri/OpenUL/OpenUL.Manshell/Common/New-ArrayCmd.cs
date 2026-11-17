namespace OpenUL.Manshell.Common;

public sealed class New_ArrayCmd : ManshellCmd
{
  public override Type[] Args { get; } =
  [
    typeof(Type),
    typeof(object[]),
    typeof(ManshellNamedArg),
  ];
  public override string[] Names { get; } = ["new-array", "new{}"];
  public override int MinimumArgs => 2;

  protected override object? OnRun(ManshellContext context, ReadOnlySpan<ManshellArg> args)
  {
    var typeArg = args[0];
    object? typeObj = typeArg.Value;

    if (typeObj is not Type elementType)
    {
      context.Throw(new InvalidOperationException("Expected a Type as the first argument"));
      return null;
    }

    var items = context.GetValue(args[1]) as object?[] ?? throw new InvalidOperationException();
    var array = Array.CreateInstance(elementType, items.Length);
    for (int i = 0; i < items.Length; i++)
    {
      array.SetValue(items[i], i);
    }

    if (args.Length > 2)
    {
      context.SetVar(args[2].Name, array);
      return null;
    }

    return array;
  }
}
