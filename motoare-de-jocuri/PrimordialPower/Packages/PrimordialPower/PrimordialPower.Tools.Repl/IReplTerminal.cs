using System;

namespace PrimordialPower.Tools.Repl;

public interface IReplTerminal
{
  Action GetExecution(string source, string[]? usings);
}
