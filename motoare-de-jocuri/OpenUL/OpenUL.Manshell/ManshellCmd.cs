using System.Reflection;
using System.Text;

namespace OpenUL.Manshell;

public abstract class ManshellCmd
{
  public abstract Type[] Args { get; }
  public abstract string[] Names { get; }
  public abstract int MinimumArgs { get; }

  public void Run(ManshellContext context, ReadOnlySpan<ManshellArg> args)
  {
    bool tooManyArgs = false;
    if (args.Length > Args.Length)
    {
      tooManyArgs = true;
    }
    if (args.Length < MinimumArgs)
    {
      context.Throw(new ManshellCmdArgException(this, -1, $"Too few arguments"));
      return;
    }
    for (int i = 0; i < int.Min(args.Length, Args.Length); i++)
    {
      var value = args[i].Value;
      if (Args[i] == typeof(object))
      {
        continue;
      }
      if (Args[i] == typeof(ManshellNamedArg) && args[i].IsName)
      {
        continue;
      }
      if (Args[i] == typeof(ManshellInfiniteArgs))
      {
        tooManyArgs = false;
        break;
      }
      if (value == null)
      {
        continue;
      }
      if (!Args[i].IsInstanceOfType(value))
      {
        context.Throw(new ManshellCmdArgException(this, i, "Invalid argument type"));
        return;
      }
    }
    if (tooManyArgs)
    {
      context.Throw(new ManshellCmdArgException(this, args.Length, $"Too many arguments"));
    }
    try
    {
      context.LastResult = OnRun(context, args);
    }
    catch (Exception ex) when (ex is TargetInvocationException { InnerException: not null } wrapped)
    {
      var inner = wrapped.InnerException;
      if (inner != null)
      {
        context.Throw(inner);
      }
    }
    catch (Exception ex)
    {
      context.Throw(ex);
    }
  }

  protected abstract object? OnRun(ManshellContext context, ReadOnlySpan<ManshellArg> args);
}