namespace Holodeck.Tests;

internal static class Eventually
{
  public static async Task<bool> TrueAsync(Func<bool> condition, int milliseconds = 5000)
  {
    DateTime until = DateTime.UtcNow.AddMilliseconds(milliseconds);
    while (DateTime.UtcNow < until)
    {
      if (condition())
      {
        return true;
      }

      await Task.Delay(50);
    }

    return condition();
  }
}
