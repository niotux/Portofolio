using System.Collections;
using System.IO.Enumeration;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace OpenUL.Manshell;

public class ManshellConsole : IManshellConsole
{
  public TextWriter StdOut => Console.Out;
  public TextReader StdIn => Console.In;
  public TextWriter StdErr => Console.Error;

  public void Clear() => Console.Clear();
  public ConsoleColor Foreground
  {
    get => Console.ForegroundColor;
    set => Console.ForegroundColor = value;
  }
  public ConsoleColor Background
  {
    get => Console.ForegroundColor;
    set => Console.ForegroundColor = value;
  }

  public (int Width, int Height) Size => (Console.WindowWidth, Console.WindowHeight);

  public void ResetColor() => Console.ResetColor();
  public string ReadLine()
  {
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.Write(Directory.GetCurrentDirectory());
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.Write(" >_ ");
    Console.ResetColor();
    return ConsoleReader.ReadColoredLine();
  }

  public void PrintResult(object? obj)
  {
    Console.ForegroundColor = ConsoleColor.DarkMagenta;
    Console.Write($"[{ManshellContext.LastName}]");
    Console.ResetColor();
    Console.Write(" = ");
    PrintObjectCore(obj, 0);
    Console.WriteLine();
  }

  public void PrintObject(object? obj) => PrintObjectCore(obj, 0);

  public void PrintException(Exception ex) => PrintExceptionCore(ex);
  private static void PrintObjectCore(object? obj, int indent)
  {
    switch (obj)
    {
      case string: goto default;

      case IEnumerable x:
        PrintEnumerable(x, indent);
        return;

      case Exception x:
        PrintExceptionCore(x);
        return;

      case Type t:
        PrintType(t);
        break;

      default:
        Console.Write(obj);
        return;
    }
  }

  private static void PrintEnumerableElementPrefix(EnumerablePrefixStyle style, int indent, int index, object? key = null)
  {
    Console.ForegroundColor = ConsoleColor.DarkGray;
    StringBuilder sb = new();
    for (int i = 1; i < indent; i++)
    {
      sb.Append("|   ");
    }
    sb.Append("|- ");
    switch (style)
    {
      default:
      case EnumerablePrefixStyle.Listed:
        Console.Write(sb.ToString());
        break;

      case EnumerablePrefixStyle.Indexed:
        sb.Append("[\u001b[92m");
        sb.Append(index);
        sb.Append("\u001b[90m]: ");
        goto default;

      case EnumerablePrefixStyle.Keyed:
        sb.Append("[\u001b[91m");
        sb.Append(key);
        sb.Append("\u001b[90m]: ");
        goto default;
    }
    Console.ResetColor();
  }

  private enum EnumerablePrefixStyle
  {
    Listed,
    Indexed,
    Keyed,
  }

  private static void PrintEnumerable(IEnumerable enumerable, int indent)
  {
    EnumerablePrefixStyle style = EnumerablePrefixStyle.Listed;

    PrintType(enumerable.GetType());
    Console.WriteLine();
    switch (enumerable)
    {
      case IList:
        style = EnumerablePrefixStyle.Indexed;
        goto default;

      case FileSystemEnumerable<string> dir:
        foreach (var entry in dir)
        {
          PrintEnumerableElementPrefix(EnumerablePrefixStyle.Listed, indent + 1, 0, null);
          Console.WriteLine(Path.GetFileName(entry.AsSpan()));
        }
        return;


      case IDictionary dict:
        foreach (var key in dict.Keys)
        {
          PrintEnumerableElementPrefix(EnumerablePrefixStyle.Keyed, indent + 1, 0, key);
          PrintObjectCore(dict[key], indent + 1);
          Console.WriteLine();
        }
        return;

      default:
        int i = 0;
        foreach (var obj in enumerable)
        {
          PrintEnumerableElementPrefix(style, indent + 1, i, null);
          PrintObjectCore(obj, indent + 1);
          Console.WriteLine();
          i++;
        }
        return;
    }
  }

  private static void PrintExceptionCore(Exception ex)
  {
    Console.ForegroundColor = ConsoleColor.DarkRed;
    Console.WriteLine(ex.ToString());
    Console.ResetColor();
  }

  private static void PrintType(Type type)
  {
    Console.Write($"\u001b[90m[\u001b[36m{type.Namespace}.{type.Name}\u001b[90m]");
    Console.ResetColor();
  }
}
