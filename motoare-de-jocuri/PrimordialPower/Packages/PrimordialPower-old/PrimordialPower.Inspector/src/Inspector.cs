using System;
using System.Collections.ObjectModel;
using System.Reflection.Metadata;

namespace PrimordialPower.Tools;

public class Inspector : IInspectorServices
{
  private Dictionary<string, Action<string[]>> commandMapping;
  private Dictionary<string, Action> argsMapping_Delete;
  public Inspector()
  {
    this.commandMapping = new()
    {
      { "/begin", Begin },
      { "/b", Begin },

      { "/end", End },
      { "/e", End },

      { "/run", Run },
      { "/r", Run },

      { "/delete", Delete },
      { "/del", Delete },
      { "/d", Delete },

      { "/edit", Edit },
      { "/ed", Edit },

      { "/load", Load },
      { "/ld", Load },
      { "/l", Load },
    };

    this.argsMapping_Delete = new()
    {
      { "-all", DeleteAll}
    };
  }



  private object lockContext = new();
  private (Type? Type, string[]? Usings) context = new();
  public (Type? Type, string[]? Usings) Context
  {
    private get
    {
      lock (lockContext)
      {
        return context;
      }
    }
    set
    {
      lock (lockContext)
      {
        context = value;
      }
    }
  }



  private List<string> builtScript = new();
  internal async Task<InterpretResult> InterpretLine(string? line)
  {
    if (string.IsNullOrWhiteSpace(line)) return new(InterpretResultState.Ignored);

    this.commandResult = new(InterpretResultState.InterpretFunction, line);

    if (line[0] == '/')
    {
      string[] args = line.Split(' ');
      if (!this.commandMapping.TryGetValue(args[0], out var command))
        return new(
          State: InterpretResultState.ErrorUnknownCommand,
          Message: $"Could not execute unknown command \"{args[0]}\"."
        );

      command.Invoke(args);
      if (
        this.commandResult.State != InterpretResultState.InterpretFunction &&
        this.commandResult.State != InterpretResultState.InterpretClass
      )
      {
        return this.commandResult;
      }
    }

    string? code = this.commandResult.Message;

    if (string.IsNullOrWhiteSpace(code)) return new(InterpretResultState.ExecutedCommand);

    if (this.scriptMode == ScriptMode.DirectScript)
    {
      await RunScript(code);
      return new(InterpretResultState.ExecutedScript, code);
    }

    if (this.scriptMode == ScriptMode.BuildingScript)
    {
      this.builtScript.Add(code);
      return this.commandResult;
    }

    return new(InterpretResultState.Ignored);
  }



  private async Task RunScript(string code)
  {
    var commandState = this.commandResult.State;
    var context = this.Context;
    var type = context.Type;
    var usings = context.Usings;

    var usingPrimordialPower = new string[]
    {
      nameof(PrimordialPower.Runtime),
      nameof(PrimordialPower.Tools),
    };


    IEnumerable<string> allUsings = usings?.Concat(usingPrimordialPower) ?? usingPrimordialPower;

    Func<Task> script;
    if (commandState == InterpretResultState.InterpretFunction)
      script = ScriptLoader.CSharp.LoadScriptFunction(code, type, allUsings);
    else
      script = ScriptLoader.CSharp.LoadScriptFunction(code, type, allUsings);

    await script.Invoke();
  }



  internal record InterpretResult(InterpretResultState State, string? Message = null);
  internal enum InterpretResultState
  {
    Ignored,
    ErrorUnknownCommand,
    ErrorUnknownArgument,
    ExecutedCommand,
    ExecutedScript,
    InterpretFunction,
    InterpretClass,
  }

  private ScriptMode scriptMode = ScriptMode.DirectScript;
  private enum ScriptMode
  {
    DirectScript,
    BuildingScript,
  }



  private InterpretResult commandResult = new(InterpretResultState.ExecutedCommand);
  private void NoArgs(string[] args)
  {
    if (args.Length >= 2) commandResult = new(InterpretResultState.ErrorUnknownArgument, args[1]);
  }



  public void Begin(string[] args)
  {
    this.scriptMode = ScriptMode.BuildingScript;
    this.commandResult = new(InterpretResultState.ExecutedCommand, args[0]);
    NoArgs(args);
  }

  public void End(string[] args)
  {
    this.scriptMode = ScriptMode.DirectScript;
    this.commandResult = new(InterpretResultState.ExecutedCommand, args[0]);
    NoArgs(args);
  }

  public void Run(string[] args)
  {
    this.commandResult = new(InterpretResultState.InterpretFunction, string.Concat(this.builtScript));
    NoArgs(args);
  }

  public void Delete(string[] args)
  {
    this.commandResult = new(InterpretResultState.ExecutedCommand, args[0]);

    if (args.Length <= 1)
    {
      this.builtScript.RemoveAt(this.builtScript.Count - 1);
      return;
    }

    for (int i = 1; i < args.Length; i++)
    {
      if (!this.argsMapping_Delete.TryGetValue(args[i], out var action)) return;
      action.Invoke();
    }
  }

  public void DeleteAll()
  {
    this.builtScript.Clear();
  }

  public void Edit(string[] args)
  {
    this.commandResult = new(InterpretResultState.ExecutedCommand, args[0]);
    NoArgs(args);
  }

  public void Load(string[] args)
  {
    this.commandResult = new(InterpretResultState.ExecutedCommand, args[0]);
    NoArgs(args);
  }
}
