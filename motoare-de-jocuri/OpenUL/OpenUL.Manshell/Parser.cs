using System.Runtime.InteropServices;

namespace OpenUL.Manshell;

internal static class Parser
{
  private static readonly (char Start, char End) ArrayPair = (Syntax.ArrayStart, Syntax.ArrayEnd);
  private static readonly (char Start, char End) TypePair = (Syntax.TypeStart, Syntax.TypeEnd);
  private static readonly (char Start, char End) StringPair = (Syntax.StringDelimiter, Syntax.StringDelimiter);

  private static readonly (char Start, char End)[] Separators =
  [
    ArrayPair,
    StringPair,
    TypePair,
  ];

  public static List<List<ManshellArg>> ParseLine(ManshellContext context, ReadOnlySpan<char> input)
  {
    List<List<ManshellArg>> commands = new();

    int start = 0;
    for (int i = 0; i <= input.Length; i++)
    {
      if (i == input.Length || input[i] == Syntax.End)
      {
        commands.Add(ParseArgGroup(context, input[start..i], false));
        start = i + 1;
      }
    }
    return commands;
  }

  private static List<ManshellArg> ParseArgGroup(ManshellContext context, ReadOnlySpan<char> input, bool resolveNames)
  {
    List<ManshellArg> results = new();
    int i = 0;
    int n = input.Length;

    while (i < n)
    {
      while (i < n && (input[i] == ' '))
      {
        i++;
      }
      if (i >= n)
      {
        break;
      }

      int start = i;
      int sepIndex = FindSeparator(input[i]);

      if (sepIndex >= 0)
      {
        // token starts with a separator -> consume until its matching end,
        // respecting nesting (for pairs where Start != End)
        var (startCh, endCh) = Separators[sepIndex];
        int depth = 1;
        i++;

        while (i < n && depth > 0)
        {
          if (input[i] == startCh && startCh != endCh)
          {
            depth++;
          }
          else if (input[i] == endCh)
          {
            depth--;
          }
          i++;
        }
      }
      else
      {
        // plain token -> consume until space, comma, or a separator start char
        i++;
        while (i < n && input[i] != ' ' && FindSeparator(input[i]) < 0)
        {
          i++;
        }
      }

      results.Add(ParseArg(context, input[start..i], resolveNames));
    }

    return results;
  }

  private static int FindSeparator(char c)
  {
    for (int j = 0; j < Separators.Length; j++)
    {
      if (Separators[j].Start == c) return j;
    }
    return -1;
  }

  public static ManshellArg ParseArg(ManshellContext context, ReadOnlySpan<char> literal, bool resolveNames)
  {
    if (literal.IsEmpty)
    {
      return new(default(object));
    }

    var cmd = context.GetCmd(literal);
    if (cmd != null)
    {
      return new(cmd);
    }

    if (resolveNames)
    {
      var var = context.GetVar(literal);
      if (var != null)
      {
        return new(var);
      }
    }

    if (IsPair(literal, ArrayPair))
    {
      var items = ParseArgGroup(context, literal[1..^1], true);
      var values = new object?[items.Count];
      for (int j = 0; j < items.Count; j++)
      {
        values[j] = items[j].Value;
      }
      return new((object?)values);
    }

    if (IsPair(literal, StringPair))
    {
      return new((object)ParseString(literal[1..^1]));
    }

    if (IsPair(literal, TypePair))
    {
      return new(ParseTypeName(context, literal[1..^1]));
    }

    if (literal.Equals(Syntax.Null, StringComparison.OrdinalIgnoreCase))
    {
      return new(default(object));
    }

    if (int.TryParse(literal, out var intValue))
    {
      return new(intValue);
    }

    if (long.TryParse(literal, out var longValue))
    {
      return new(longValue);
    }

    if (uint.TryParse(literal, out var uintValue))
    {
      return new(uintValue);
    }

    if (ulong.TryParse(literal, out var ulongValue))
    {
      return new(ulongValue);
    }

    if (double.TryParse(literal, out var doubleValue))
    {
      return new(doubleValue);
    }

    if (float.TryParse(literal, out var floatValue))
    {
      return new(floatValue);
    }

    if (bool.TryParse(literal, out var boolValue))
    {
      return new(boolValue);
    }

    return new(literal.ToString());
  }

  private static bool IsPair(ReadOnlySpan<char> literal, (char Start, char End) pair)
  {
    return literal[0] == pair.Start && literal[^1] == pair.End && literal.Length >= 2;
  }

  private static string ParseString(ReadOnlySpan<char> literal)
  {
    return new(literal);
  }

  // "Base[Arg1,Arg2]" recurses into each arg using this same grammar (Arg1/Arg2 may
  // themselves be "Base[...]"). "Base[]" / "Base[,]" (all slots empty) resolves the open
  // generic definition instead of a closed MakeGenericType.
  private static Type? ParseTypeName(ManshellContext context, ReadOnlySpan<char> name)
  {
    int lb = name.IndexOf(Syntax.TypeStart);
    if (lb < 0)
    {
      return ResolveType(context, name);
    }

    var baseName = name[..lb];
    var argsSpan = name[(lb + 1)..^1];

    int arity = CountTopLevelArgs(argsSpan, out bool allEmpty);
    if (allEmpty)
    {
      return ResolveType(context, $"{baseName}`{arity}");
    }

    var typeArgs = new Type[arity];
    int depth = 0;
    int start = 0;
    int argIndex = 0;
    for (int i = 0; i <= argsSpan.Length; i++)
    {
      if (i == argsSpan.Length || (argsSpan[i] == ' ' && depth == 0))
      {
        var argType = ParseTypeName(context, argsSpan[start..i]);
        if (argType == null)
        {
          return null;
        }
        typeArgs[argIndex++] = argType;
        start = i + 1;
        continue;
      }
      if (argsSpan[i] == Syntax.TypeStart)
      {
        depth++;
      }
      else if (argsSpan[i] == Syntax.TypeEnd)
      {
        depth--;
      }
    }

    var genericDef = ResolveType(context, $"{baseName}`{arity}");
    if (genericDef == null)
    {
      return null;
    }

    try
    {
      return genericDef.MakeGenericType(typeArgs);
    }
    catch (ArgumentException)
    {
      return null;
    }
  }

  // Counts top-level comma-separated slots inside a generic arg list (respecting nested
  // brackets), and reports whether every slot is empty (open generic, e.g. "" or ",").
  private static int CountTopLevelArgs(ReadOnlySpan<char> args, out bool allEmpty)
  {
    allEmpty = true;
    int depth = 0;
    int start = 0;
    int count = 0;
    for (int i = 0; i <= args.Length; i++)
    {
      if (i == args.Length || (args[i] == ' ' && depth == 0))
      {
        if (i > start)
        {
          allEmpty = false;
        }
        count++;
        start = i + 1;
        continue;
      }
      if (args[i] == Syntax.TypeStart)
      {
        depth++;
      }
      else if (args[i] == Syntax.TypeEnd)
      {
        depth--;
      }
    }
    return count;
  }

  private static Type? ResolveType(ManshellContext context, ReadOnlySpan<char> name)
  {
    string nameStr = name.ToString();
    foreach (var asm in context.Assemblies)
    {
      var type = asm.GetType(nameStr);
      if (type != null)
      {
        return type;
      }
    }
    return null;
  }
}