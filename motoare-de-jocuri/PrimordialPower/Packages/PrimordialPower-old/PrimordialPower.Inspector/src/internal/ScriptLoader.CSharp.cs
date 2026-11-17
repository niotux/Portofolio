using System;
using System.Collections;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Scripting;

namespace PrimordialPower.Tools;

internal static class ScriptLoader
{
  public static class CSharp
  {
    private static Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
    public static Func<Task> LoadScriptFunction(string? code, Type? context, IEnumerable<string>? usings)
    {

      List<string> allUsings = new()
      {
        "System",
        "System.Collections",
        "System.Collections.Generic",
        "System.Collections.Concurrent",
        "System.Threading",
        "System.Threading.Tasks",
        "System.Text",
        "System.Linq",
      };

      if (usings is not null) allUsings.AddRange(usings);

      string? typeNamespace = context?.Namespace;
      if (typeNamespace is not null) allUsings.Add(typeNamespace);

      var script = CSharpScript.Create<Task>(
        code: code,
        options: ScriptOptions.Default
          .AddImports(allUsings)
          .AddReferences(assemblies)
      );

      return () => script.CreateDelegate().Invoke();
    }
  }
}
