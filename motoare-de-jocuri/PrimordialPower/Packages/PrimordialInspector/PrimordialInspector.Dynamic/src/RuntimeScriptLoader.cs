using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace PrimordialInspector.Scripting;

public class RuntimeScriptLoader
{
  private static int _scriptCounter = 0;
  private static readonly ConcurrentDictionary<(Type TargetType, string ScriptHash), Type> _cachedHosts = new();


  public Func<Task> LoadScript(object instance, string code, string[]? usings = null)
  {
    if (instance == null)
      throw new ArgumentNullException(nameof(instance));

    var targetType = instance.GetType();
    var allUsingDecl = GetUsingDecl(usings, targetType);
    return CompileScript(instance, targetType, code, allUsingDecl);
  }

  private List<string> GetUsingDecl(string[]? additionalUsings, Type? instanceType = null)
  {
    var usings = new List<string>
    {
      "System",
      "System.Collections.Generic",
      "System.Linq",
      "System.Text",
      "System.Threading",
      "System.Threading.Tasks",
      "PrimordialPower.Core"
    };

    string instanceNamespace = instanceType?.Namespace ?? string.Empty;
    if (instanceNamespace.Length != 0)
    {
      usings.Add(instanceNamespace);
    }

    if (additionalUsings != null)
    {
      foreach (var @using in additionalUsings)
      {
        if (!string.IsNullOrWhiteSpace(@using))
        {
          usings.Add(@using.Trim());
        }
      }
    }

    return usings.Select((string u) => $"using {u};").ToList();
  }



  private Func<Task> GetScriptDelegate(Type type)
  {
    object scriptInstance = Activator.CreateInstance(type)!;
    MethodInfo method = type.GetMethod("Execute")!;
    return () => (Task)method.Invoke(scriptInstance, null)!;
  }



  private Func<Task> CompileScript(object instance, Type targetType, string code, List<string> usings)
  {
    var fullSource = string.Join("\n", usings) + "\n" + code;
    var scriptHash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(fullSource)));

    if (_cachedHosts.TryGetValue((targetType, scriptHash), out var cachedType))
    {
      return GetScriptDelegate(cachedType);
    }

    var scriptId = Interlocked.Increment(ref _scriptCounter);
    var hostClassName = $"ScriptHost_{targetType.Name}_{scriptId}";

    var source = new StringBuilder();
    foreach (var u in usings)
    {
      source.AppendLine(u);
    }
    source.AppendLine();
    source.AppendLine($"public class {hostClassName} : {targetType.FullName}");
    source.AppendLine("{");
    source.AppendLine($"  public {hostClassName}() {{}}");
    source.AppendLine($"  public async Task Execute({targetType.FullName} target)");
    source.AppendLine("  {");
    foreach (var line in code.Split('\n'))
    {
      source.AppendLine($"    {line}");
    }
    source.AppendLine("  }");
    source.AppendLine("}");

    var syntaxTree = CSharpSyntaxTree.ParseText(source.ToString());
    var references = GetReferences(targetType);

    var compilation = CSharpCompilation.Create(
      $"DynamicScript_{scriptId}",
      [syntaxTree],
      references,
      new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
    );

    using var ms = new MemoryStream();
    var result = compilation.Emit(ms);

    if (!result.Success)
    {
      var errors = string.Join("\n", result.Diagnostics
        .Where(d => d.Severity == DiagnosticSeverity.Error)
        .Select(d => d.GetMessage()));
      throw new InvalidOperationException($"Compilation failed:\n{errors}\n\nSource:\n{source}");
    }

    ms.Seek(0, SeekOrigin.Begin);
    var assembly = Assembly.Load(ms.ToArray());
    var type = assembly.GetType(hostClassName)!;

    _cachedHosts[(targetType, scriptHash)] = type;

    return GetScriptDelegate(type);
  }

  private List<MetadataReference> GetReferences(Type targetType)
  {
    var references = new List<MetadataReference>
    {
      MetadataReference.CreateFromFile(typeof(RuntimeScriptLoader).Assembly.Location),
      MetadataReference.CreateFromFile(targetType.Assembly.Location),
      MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
      MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
      MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location)
    };

    var coreDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
    var coreLibs = new[] { "System.Runtime.dll", "System.Collections.dll" };

    foreach (var lib in coreLibs)
    {
      var path = Path.Combine(coreDir, lib);
      if (File.Exists(path))
        references.Add(MetadataReference.CreateFromFile(path));
    }

    foreach (var refAsm in targetType.Assembly.GetReferencedAssemblies())
    {
      try
      {
        var asm = Assembly.Load(refAsm);
        if (!string.IsNullOrEmpty(asm.Location))
          references.Add(MetadataReference.CreateFromFile(asm.Location));
      }
      catch { }
    }

    return references;
  }
}
