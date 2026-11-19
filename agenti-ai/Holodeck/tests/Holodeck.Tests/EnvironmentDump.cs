namespace Holodeck.Tests;

// Reads the KEY=value lines a test agent wrote with `env`.
internal static class EnvironmentDump
{
  public static string? Read(string file, string name)
  {
    if (!File.Exists(file))
    {
      return null;
    }

    foreach (string line in File.ReadAllLines(file))
    {
      ReadOnlySpan<char> text = line.AsSpan();
      int equals = text.IndexOf('=');
      if (equals > 0 && text[..equals].SequenceEqual(name))
      {
        return text[(equals + 1)..].ToString();
      }
    }

    return null;
  }
}
