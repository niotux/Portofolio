using System.Text;

namespace Holodeck.Web.Computers;

// Launch arguments name files in the agent, its library, or a declared dependency package.
public static class Placeholders
{
  private const string AgentToken = "${Agent}";
  private const string LibraryToken = "${Library}";
  private const string DependencyPrefix = "${Dependency:";

  public static string Expand(string value, string agent, string library, IReadOnlyDictionary<string, string>? dependencies = null)
  {
    ReadOnlySpan<char> rest = value.AsSpan();
    StringBuilder result = new(value.Length);
    while (!rest.IsEmpty)
    {
      int next = rest.IndexOf("${", StringComparison.Ordinal);
      if (next < 0)
      {
        result.Append(rest);
        break;
      }

      result.Append(rest[..next]);
      rest = rest[next..];
      if (rest.StartsWith(AgentToken, StringComparison.Ordinal))
      {
        result.Append(agent);
        rest = rest[AgentToken.Length..];
      }
      else if (rest.StartsWith(LibraryToken, StringComparison.Ordinal))
      {
        result.Append(library);
        rest = rest[LibraryToken.Length..];
      }
      else if (rest.StartsWith("${Dependency", StringComparison.Ordinal))
      {
        result.Append(ReadDependency(ref rest, dependencies));
      }
      else
      {
        result.Append(rest[0]);
        rest = rest[1..];
      }
    }

    return result.ToString();
  }

  private static string ReadDependency(ref ReadOnlySpan<char> rest, IReadOnlyDictionary<string, string>? dependencies)
  {
    int end = rest.IndexOf('}');
    if (end < 0 || !rest.StartsWith(DependencyPrefix, StringComparison.Ordinal))
    {
      throw new ArgumentException("Dependency placeholders must use ${Dependency:libraryId}.");
    }

    string id = rest[DependencyPrefix.Length..end].ToString();
    if (id.Length == 0 || dependencies is null || !dependencies.TryGetValue(id, out string? directory))
    {
      throw new ArgumentException($"Dependency placeholder '{id}' requires a declared local library dependency.");
    }

    rest = rest[(end + 1)..];
    return directory;
  }
}
