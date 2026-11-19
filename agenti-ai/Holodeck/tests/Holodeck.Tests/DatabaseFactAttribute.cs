using Xunit;

namespace Holodeck.Tests;

public sealed class DatabaseFactAttribute : FactAttribute
{
  public const string ConnectionVariable = "HOLODECK_TEST_DATABASE";

  public DatabaseFactAttribute()
  {
    if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionVariable)))
    {
      Skip = $"Set {ConnectionVariable} to a disposable PostgreSQL database.";
    }
  }
}
