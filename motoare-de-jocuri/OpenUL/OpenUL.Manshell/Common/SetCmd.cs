namespace OpenUL.Manshell.Common;

public sealed class SetCmd : ManshellCmd
{
  public override Type[] Args { get; } =
  [
    typeof(object),
    typeof(object),
    typeof(object),
  ];
  public override string[] Names { get; } = ["set"];
  public override int MinimumArgs => 2;

  protected override object? OnRun(ManshellContext context, ReadOnlySpan<ManshellArg> args)
  {
    if (args.Length == 2)
    {
      var name = args[0].Name;
      var value = Resolve(context, args[1]);
      context.SetVar(name, value);
      return null;
    }

    var self = args[0];
    object? obj = context.GetValue(self);

    if (obj == null)
    {
      context.Throw(new NullReferenceException());
      return null;
    }

    var propName = args[1].Name;
    var propValue = Resolve(context, args[2]);

    Type type = obj as Type ?? obj.GetType();
    var propInfo = type.GetProperty(propName);
    if (propInfo == null || !propInfo.CanWrite)
    {
      context.Throw(new MemberAccessException());
      return null;
    }

    propInfo.SetValue((object)type == obj && propInfo.SetMethod!.IsStatic ? null : obj, propValue);
    return null;
  }

  private static object? Resolve(ManshellContext context, ManshellArg arg)
  {
    return arg.IsName ? context.GetVar(arg.Name) : arg.Value;
  }
}
