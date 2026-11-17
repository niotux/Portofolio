namespace LAN.Logging;

public enum LogLevel
{
  None,
  Info,
  Warning,
  Error,
  Fatal,
}

public readonly struct ErrorMessage
{
  public LogLevel Level { get; }
  public string Message { get; }

  public ErrorMessage(LogLevel level, string message)
  {
    Level = level;
    Message = message;
  }

  public static implicit operator ErrorMessage(string message) => new(LogLevel.Info, message);
}