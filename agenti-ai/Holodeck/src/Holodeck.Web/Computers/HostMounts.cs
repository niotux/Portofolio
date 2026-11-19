using Holodeck.Web.Rooms;

namespace Holodeck.Web.Computers;

// Holodeck sees the data and library folders at its own mount points; Docker needs where they are on the host.
public static class HostMounts
{
  public static string HostPathOf(string path, HostPaths host, RoomPaths paths)
  {
    string? mapped = Rebase(path, paths.Root, host.Data) ?? Rebase(path, paths.Libraries, host.Libraries);
    if (mapped is null)
    {
      throw new InvalidOperationException($"{path} is outside Holodeck's data and library folders.");
    }

    return mapped;
  }

  private static string? Rebase(string path, string from, string to)
  {
    ReadOnlySpan<char> value = path.AsSpan();
    if (!value.StartsWith(from, StringComparison.Ordinal))
    {
      return null;
    }

    ReadOnlySpan<char> rest = value[from.Length..];
    if (!rest.IsEmpty && rest[0] != '/')
    {
      return null;
    }

    return string.Concat(to.AsSpan(), rest);
  }
}
