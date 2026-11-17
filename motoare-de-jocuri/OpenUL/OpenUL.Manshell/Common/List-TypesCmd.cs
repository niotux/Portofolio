using System.Reflection;

namespace OpenUL.Manshell.Common;

public sealed class List_TypesCmd : ManshellCmd
{
  public override Type[] Args { get; } =
  [
    typeof(string),
  ];
  public override string[] Names { get; } = ["list-types"];
  public override int MinimumArgs => 0;

  protected override object? OnRun(ManshellContext context, ReadOnlySpan<ManshellArg> args)
  {
    var fragment = args.Length == 1
      ? (context.GetValue(args[0]) as string ?? string.Empty)
      : string.Empty;

    foreach (var asm in context.Assemblies)
    {
      Type?[] types;
      try
      {
        types = asm.GetTypes();
      }
      catch (ReflectionTypeLoadException ex)
      {
        types = ex.Types;
      }

      foreach (var type in types)
      {
        if (type?.FullName?.Contains(fragment, StringComparison.OrdinalIgnoreCase) == true)
        {
          context.Console.PrintObject(type);
          context.Console.StdOut.WriteLine();
        }
      }
    }

    return null;
  }
}
