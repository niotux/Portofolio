using System;
using System.Collections.Concurrent;
namespace LAN.Logging;

public class Logger
{
  private const string Reset = "\x1b[0m";
  private const string Grey = "\x1b[90m";
  private const string Token = "\x1b[35m"; // magenta — matches the old CLIK TokenF

  private static volatile LogLevel MaxLoggedLevel_Field;
  public static LogLevel MaxLoggedLevel => MaxLoggedLevel_Field;

  // The "[ Level ]" tag, reproduced from the old CLIK prefixes: grey brackets,
  // coloured level word, everything else default.
  private static string Prefix(LogLevel level) => level switch
  {
    LogLevel.Error => $"{Grey}[ \x1b[31mError{Grey} ]{Reset}",
    LogLevel.Warning => $"{Grey}[ \x1b[33mWarn{Grey} ]{Reset}",
    LogLevel.Info => $"{Grey}[ \x1b[32mInfo{Grey} ]{Reset}",
    _ => $"{Grey}[ \x1b[34m???{Grey} ]{Reset}",
  };

  private record struct LogMessage
  (
    ErrorMessage Message,
    SourceView Context,
    SourceView Fault
  );

  private readonly ConcurrentQueue<LogMessage> Messages = new();

  public void Log(ErrorMessage error, SourceView fault = default, SourceView context = default)
    => Messages.Enqueue(new(error, context, fault));

  public void WriteMessages(Action<string> writer)
  {
    foreach (var m in Messages)
    {
      Write(writer, m);
    }
  }

  private static void Write(Action<string> writer, LogMessage log)
  {
    if (log.Message.Level > MaxLoggedLevel)
    {
      MaxLoggedLevel_Field = log.Message.Level;
    }

    string prefix = Prefix(log.Message.Level);
    string message = log.Message.Message;

    if (!log.Context.IsEmpty && !log.Fault.IsEmpty)
    {
      writer($"{prefix} {message} {Token}'{log.Context}'{Reset}: {Token}'{log.Fault}'{Reset} {Location(log.Fault)}");
      return;
    }
    if (!log.Fault.IsEmpty)
    {
      writer($"{prefix} {message}: {Token}'{log.Fault}'{Reset} {Location(log.Fault)}");
      return;
    }
    if (!log.Context.IsEmpty)
    {
      writer($"{prefix} {message} {Token}'{log.Context}'{Reset} {Location(log.Context)}");
      return;
    }
    writer($"{prefix} {message}");
  }

  private static string Location(SourceView where)
    => $"{Grey}[Line {where.Line}, Char {where.Character}]{Reset}";
}
