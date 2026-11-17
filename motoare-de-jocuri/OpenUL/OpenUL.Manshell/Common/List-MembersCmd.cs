using System.Reflection;

namespace OpenUL.Manshell.Common;

public sealed class List_MembersCmd : ManshellCmd
{
  public override Type[] Args { get; } =
  [
    typeof(object),
  ];
  public override string[] Names { get; } = ["list-members"];
  public override int MinimumArgs => 1;

  protected override object? OnRun(ManshellContext context, ReadOnlySpan<ManshellArg> args)
  {
    var self = args[0];
    object? obj = context.GetValue(self);


    if (obj == null)
    {
      context.Throw(new NullReferenceException());
      return null;
    }

    Type type = obj as Type ?? obj.GetType();

    foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Static))
    {
      context.Console.Foreground = ConsoleColor.DarkBlue;
      context.Console.StdOut.Write($"prop static ");
      printProp(context, prop);
      context.Console.StdOut.WriteLine();
    }

    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
    {
      context.Console.Foreground = ConsoleColor.DarkBlue;
      context.Console.StdOut.Write($"meth static ");
      printMeth(context, method);
    }

    foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
    {
      context.Console.Foreground = ConsoleColor.DarkBlue;
      context.Console.StdOut.Write($"prop ");
      printProp(context, prop);
    }

    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
    {
      context.Console.Foreground = ConsoleColor.DarkBlue;
      context.Console.StdOut.Write($"meth ");
      printMeth(context, method);
    }

    static void printProp(ManshellContext context, PropertyInfo prop)
    {
      context.Console.PrintObject(prop.PropertyType);
      context.Console.StdOut.Write($" {prop.Name} ");

      context.Console.Foreground = ConsoleColor.DarkBlue;
      if (prop.CanRead)
      {
        context.Console.StdOut.Write("get ");
      }
      if (prop.CanWrite)
      {
        context.Console.StdOut.Write("set ");
      }
      context.Console.StdOut.WriteLine();
    }

    static void printMeth(ManshellContext context, MethodInfo method)
    {
      context.Console.Foreground = ConsoleColor.DarkBlue;
      if (method.IsSpecialName)
      {
        context.Console.StdOut.Write("special ");
      }
      context.Console.PrintObject(method.ReturnType);
      context.Console.Foreground = ConsoleColor.Yellow;
      context.Console.StdOut.Write($" {method.Name} ");
      context.Console.Foreground = ConsoleColor.Yellow;
      context.Console.StdOut.Write('(');
      bool sep = false;
      foreach (var param in method.GetParameters())
      {
        if (sep)
        {
          context.Console.Foreground = ConsoleColor.Yellow;
          context.Console.StdOut.Write(", ");
        }
        sep = true;

        context.Console.PrintObject(param.ParameterType);
        context.Console.StdOut.Write(' ');
        context.Console.Foreground = ConsoleColor.White;
        context.Console.StdOut.Write(param.Name);
      }
      context.Console.Foreground = ConsoleColor.Yellow;
      context.Console.StdOut.WriteLine(')');
    }
    context.Console.ResetColor();
    return null;
  }
}
