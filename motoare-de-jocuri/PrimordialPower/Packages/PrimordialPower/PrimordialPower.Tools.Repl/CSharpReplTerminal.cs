using System;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace PrimordialPower.Tools.Repl;

public class CSharpReplTerminal : IReplTerminal
{
  private ulong assemblyCount = 0;
  private readonly List<MetadataReference> references =
  [
    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
    MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
    MetadataReference.CreateFromFile(Assembly.Load("System.Console").Location),
    MetadataReference.CreateFromFile(Assembly.Load("System.Collections").Location),
    MetadataReference.CreateFromFile(Assembly.Load("System.Linq").Location),
    MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
    MetadataReference.CreateFromFile(Assembly.GetExecutingAssembly().Location),
  ];
  public Action GetExecution(string source, string[]? usings)
  {
    string? usingsCode = null;
    if (usings is not null && usings.Length > 0)
    {
      StringBuilder sb = new(8 * usings.Length);
      foreach (var use in usings)
      {
        if (use.Length == 0) continue;
        sb.Append("using ");
        sb.Append(use);
        sb.Append(';');
        sb.Append('\n');
      }

      usingsCode = sb.ToString();
    }
    string wrappedCode =
    $$"""
    {{usingsCode}}
    public class ReplHost
    {
      public static void Execute()
      {
        {{source}}
      }
    }
    """;

    SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(wrappedCode);
    string assemblyName = $"ReplHostAssembly{this.assemblyCount}";

    CSharpCompilation compilation = CSharpCompilation.Create(
      assemblyName: assemblyName,
      syntaxTrees: [syntaxTree],
      references: this.references,
      options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
    );

    using MemoryStream stream = new();
    EmitResult result = compilation.Emit(stream);
    if (!result.Success)
    {
      IEnumerable<Diagnostic> failures = result.Diagnostics.Where(d => d.IsWarningAsError || d.Severity == DiagnosticSeverity.Error);
      string errors = string.Join("\n", failures.Select(f => $"{f.Id}: {f.GetMessage()}"));
      throw new InvalidOperationException($"Compilation failed:\n{errors}");
    }

    stream.Seek(0, SeekOrigin.Begin);
    Assembly asm = Assembly.Load(stream.ToArray());
    Type? hostType = asm.GetType("ReplHost");
    MethodInfo? hostMethod = hostType?.GetMethod("Execute");

    return hostMethod?.CreateDelegate(typeof(Action)) as Action ?? Empty;
  }

  private static void Empty() { }
}
