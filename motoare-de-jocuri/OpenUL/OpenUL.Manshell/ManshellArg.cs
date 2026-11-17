namespace OpenUL.Manshell;

public readonly struct ManshellArg
{
  public ManshellArg(object? value)
  {
    Value = value;
    IsName = false;
  }

  public ManshellArg(string name)
  {
    Value = name;
    IsName = true;
  }


  public object? Value { get; }
  public bool IsName { get; }
  public string Name
  {
    get
    {
      if (Value is not string x || IsName == false)
      {
        throw new InvalidOperationException("Arg must be a name");
      }
      return x;
    }
  }

  public static (string Text, bool Special) GetArgDisplay(Type arg)
  {
    if (arg == typeof(ManshellNamedArg))
    {
      return ("$name", true);
    }
    if (arg == typeof(ManshellInfiniteArgs))
    {
      return ("...", true);
    }
    return (arg.Name, false);
  }

}

public static class ManshellNamedArg;
public static class ManshellInfiniteArgs;