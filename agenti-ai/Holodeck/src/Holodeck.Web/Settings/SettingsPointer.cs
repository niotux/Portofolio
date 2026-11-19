using System.Text;

namespace Holodeck.Web.Settings;

// JSON Pointers (RFC 6901) naming values in a settings document: "/model/name"; "" is the whole document.
public static class SettingsPointer
{
  public static string Child(string path, string name)
  {
    StringBuilder pointer = new(path.Length + name.Length + 1);
    pointer.Append(path).Append('/');
    foreach (char character in name)
    {
      if (character == '~')
      {
        pointer.Append("~0");
      }
      else if (character == '/')
      {
        pointer.Append("~1");
      }
      else
      {
        pointer.Append(character);
      }
    }

    return pointer.ToString();
  }

  public static List<string> Parse(string pointer)
  {
    ReadOnlySpan<char> rest = pointer.AsSpan();
    if (rest.Length == 0 || rest[0] != '/')
    {
      throw new ArgumentException($"'{pointer}' is not a settings path.");
    }

    List<string> names = new();
    while (rest.Length > 0)
    {
      rest = rest[1..];
      int end = rest.IndexOf('/');
      ReadOnlySpan<char> segment = end < 0 ? rest : rest[..end];
      names.Add(Unescape(segment, pointer));
      rest = end < 0 ? ReadOnlySpan<char>.Empty : rest[end..];
    }

    return names;
  }

  private static string Unescape(ReadOnlySpan<char> segment, string pointer)
  {
    StringBuilder name = new(segment.Length);
    for (int index = 0; index < segment.Length; index++)
    {
      if (segment[index] != '~')
      {
        name.Append(segment[index]);
        continue;
      }

      bool hasNext = index + 1 < segment.Length;
      if (hasNext && segment[index + 1] == '0')
      {
        name.Append('~');
      }
      else if (hasNext && segment[index + 1] == '1')
      {
        name.Append('/');
      }
      else
      {
        throw new ArgumentException($"'{pointer}' is not a settings path.");
      }

      index++;
    }

    return name.ToString();
  }
}
