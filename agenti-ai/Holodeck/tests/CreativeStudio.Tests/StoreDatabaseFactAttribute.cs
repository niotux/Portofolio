using Xunit;

namespace CreativeStudio.Tests;

public sealed class StoreDatabaseFactAttribute : FactAttribute
{
  public const string ConnectionVariable = "HOLODECK_TEST_DATABASE";

  public StoreDatabaseFactAttribute()
  {
    if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionVariable)))
    {
      Skip = "Set HOLODECK_TEST_DATABASE to a disposable PostgreSQL database.";
    }
  }
}
