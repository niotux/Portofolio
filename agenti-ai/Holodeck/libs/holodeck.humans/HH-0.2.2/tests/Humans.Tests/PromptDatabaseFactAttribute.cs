using Xunit;

namespace Humans.Tests;

public sealed class PromptDatabaseFactAttribute : FactAttribute
{
  public PromptDatabaseFactAttribute()
  {
    if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HOLODECK_TEST_DATABASE")))
    {
      Skip = "Set HOLODECK_TEST_DATABASE to a disposable PostgreSQL database.";
    }
  }
}
