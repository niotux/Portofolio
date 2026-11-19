namespace Holodeck.Web.Rooms;

// Member names appear in folder paths on other agents' tables, so they must be safe as a folder name.
public static class AgentNames
{
  public static string Validate(string? name)
  {
    ReadOnlySpan<char> trimmed = name.AsSpan().Trim();
    if (trimmed.IsEmpty)
    {
      throw new ArgumentException("A name is required.");
    }

    if (trimmed.Length > RoomLimits.MaxNameLength)
    {
      throw new ArgumentException($"Use a name of up to {RoomLimits.MaxNameLength} characters.");
    }

    if (trimmed[0] == '.')
    {
      throw new ArgumentException("A name cannot start with a dot.");
    }

    foreach (char character in trimmed)
    {
      bool isSeparator = character == '/' || character == '\\' || character == ':';
      if (isSeparator || char.IsControl(character))
      {
        throw new ArgumentException("A name cannot contain / \\ : or control characters.");
      }
    }

    return trimmed.ToString();
  }
}
