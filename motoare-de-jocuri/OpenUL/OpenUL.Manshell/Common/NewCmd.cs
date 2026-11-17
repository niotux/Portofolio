namespace OpenUL.Manshell.Common;

public sealed class NewCmd : ManshellCmd
{
  public override Type[] Args { get; } =
  [
    typeof(Type),
    typeof(object[]),
    typeof(ManshellNamedArg),
  ];
  public override string[] Names { get; } = ["new"];
  public override int MinimumArgs => 2;

  protected override object? OnRun(ManshellContext context, ReadOnlySpan<ManshellArg> args)
  {
    var typeArg = args[0];
    object? typeObj = context.GetValue(typeArg);


    if (typeObj is not Type type)
    {
      context.Throw(new InvalidOperationException("Expected a Type as the first argument"));
      return null;
    }

    var ctorParams = context.GetValue(args[1]) as object?[] ?? throw new InvalidOperationException();
    var typeParams = new Type[ctorParams.Length];
    for (int i = 0; i < ctorParams.Length; i++)
    {
      typeParams[i] = ctorParams[i]?.GetType() ?? typeof(object);
    }

    object instance;
    if (ctorParams.Length == 0)
    {
      instance = Activator.CreateInstance(type)!;
    }
    else
    {
      var ctor = type.GetConstructor(typeParams);
      if (ctor == null)
      {
        context.Throw(new MissingMethodException(type.FullName, ".ctor"));
        return null;
      }

      instance = ctor.Invoke(ctorParams);
    }
    if (args.Length > 2)
    {
      context.SetVar(args[2].Name, instance);
      return null;
    }

    return instance;
  }
}
