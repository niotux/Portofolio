using System.Text.RegularExpressions;

namespace Humans;

public static partial class HumanVersion
{
  public static string Require(string version)
  {
    if (string.IsNullOrWhiteSpace(version) || !Pattern().IsMatch(version))
    {
      throw new ArgumentException("An HH version has the form HH-X.X.X, for example HH-0.1.1.");
    }
    return version;
  }

  [GeneratedRegex("^HH-(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)$")]
  private static partial Regex Pattern();
}
