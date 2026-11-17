using System.Reflection;

namespace OpenUL.Manshell;

public class ManshellContext
{
  public const string LastName = "@";
  public bool IsRunning { get; set; } = true;
  public object? LastResult
  {
    get;
    set
    {
      NamedVars["@"] = value;
      field = value;
    }
  }
  public Exception? LastException { get; private set; }
  public void Throw(Exception ex) => LastException = ex;
  public void ClearException() => LastException = null;

  public IManshellConsole Console { get; }

  private readonly Dictionary<string, object?> NamedVarsDict;
  private readonly Dictionary<string, object?>.AlternateLookup<ReadOnlySpan<char>> NamedVars;
  private readonly Dictionary<string, ManshellCmd> CommandsDict;
  private readonly HashSet<ManshellCmd> CommandsSet;
  private readonly Dictionary<string, ManshellCmd>.AlternateLookup<ReadOnlySpan<char>> Commands;

  public ManshellContext() : this(new ManshellConsole()) { }
  public ManshellContext(IManshellConsole console)
  {
    Console = console;

    NamedVarsDict = new();
    NamedVars = NamedVarsDict.GetAlternateLookup<ReadOnlySpan<char>>();
    CommandsSet = new();
    CommandsDict = new();
    Commands = CommandsDict.GetAlternateLookup<ReadOnlySpan<char>>();
  }

  public IReadOnlyDictionary<string, object?> Vars => NamedVarsDict;
  public IReadOnlySet<ManshellCmd> Cmds => CommandsSet;



  public void SetVar(ReadOnlySpan<char> name, object? value)
  {
    if (name.SequenceEqual(LastName))
    {
      return;
    }
    if (name.IsEmpty)
    {
      return;
    }
    if (value == null)
    {
      NamedVars.Remove(name);
      return;
    }
    NamedVars[name] = value;
  }

  public object? GetVar(ReadOnlySpan<char> name)
  {
    if (name.IsEmpty)
    {
      return null;
    }

    NamedVars.TryGetValue(name, out var value);
    return value;
  }
  public void RemoveVar(ReadOnlySpan<char> name)
  {
    if (name.IsEmpty)
    {
      return;
    }
    NamedVars.Remove(name);
  }

  public List<Assembly> Assemblies { get; } = new();


  public void AddCmd<TCmd>()
    where TCmd : ManshellCmd, new()
  {
    TCmd cmd = new();
    AddCmd(cmd);
  }

  public void AddCmd(ManshellCmd cmd)
  {
    if (!CommandsSet.Add(cmd))
    {
      return;
    }

    foreach (var name in cmd.Names)
    {
      if (!Commands.TryAdd(name, cmd))
      {
        throw new InvalidOperationException("Name collision on Manshell commands");
      }
    }
  }

  public void AddCmds(IEnumerable<ManshellCmd> cmds)
  {
    foreach (var cmd in cmds)
    {
      AddCmd(cmd);
    }
  }


  public ManshellCmd? GetCmd(ReadOnlySpan<char> name)
  {
    if (name.IsEmpty)
    {
      return null;
    }

    Commands.TryGetValue(name, out var value);
    return value;
  }


  public void RemoveCmd<TCmd>()
    where TCmd : ManshellCmd, new()
  {
    TCmd cmd = new();
    ManshellCmd? cmdInst = null;
    foreach (var name in cmd.Names)
    {
      Commands.Remove(name, out _, out cmdInst);
    }
    if (cmdInst != null)
    {
      CommandsSet.Remove(cmdInst);
    }
  }

  public void RemoveCmd(ManshellCmd cmd)
  {
    foreach (var name in cmd.Names)
    {
      Commands.Remove(name);
    }
  }

  public object? GetValue(ManshellArg arg)
  {
    return arg.IsName
      ? GetVar(arg.Name) ?? arg.Name
      : arg.Value;
  }
}