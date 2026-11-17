using System.Reflection;
using System.Runtime.InteropServices;
using VB = Microsoft.VisualBasic;

namespace OpenUL.Manshell.Common;

public sealed class GetCmd : ManshellCmd
{
  public override Type[] Args { get; } =
  [
    typeof(object),
    typeof(ManshellNamedArg),
    typeof(ManshellNamedArg),
  ];
  public override string[] Names { get; } = ["get"];
  public override int MinimumArgs => 1;

  protected override object? OnRun(ManshellContext context, ReadOnlySpan<ManshellArg> args)
  {
    var self = args[0];
    object? obj = context.GetValue(self);


    if (args.Length == 1)
    {
      return obj;
    }

    var method = args[1].Name;


    if (obj == null)
    {
      context.Throw(new NullReferenceException());
      return null;
    }

    Type type = obj as Type ?? obj.GetType();
    var methInfo = type.GetProperty(method);
    if (methInfo != null && methInfo.CanRead)
    {
      var result = methInfo.GetMethod?.Invoke((object)type == obj && methInfo.GetMethod.IsStatic ? null : obj, null);
      if (args.Length == 3)
      {
        context.SetVar(args[2].Name, result);
        return null;
      }
      return result;
    }

    context.Throw(new MemberAccessException());
    return null;
  }
}
