namespace Holodeck.Web.Computers;

// Checks what the user asks to grant and returns it in stored form. Host folders must lie inside a configured
// root and never overlap Holodeck's own folders; paths are checked as written, since the host's symlinks are not
// visible from here. New ports get the first free host port in the configured range.
public static class GrantRules
{
  private const int MaxFolderNameLength = 40;
  private const int MaxPort = 65535;

  public static AgentGrants Validate(AgentGrants requested, AgentGrants current, ComputerOptions options, HostPaths host, List<int> takenHostPorts)
  {
    List<PortGrant> ports = ValidatePorts(requested.Ports, current.Ports, options, takenHostPorts);
    List<FolderGrant> folders = ValidateFolders(requested.Folders, options, host);
    return new AgentGrants(ports, folders);
  }

  private static List<PortGrant> ValidatePorts(List<PortGrant> requested, List<PortGrant> current, ComputerOptions options, List<int> takenHostPorts)
  {
    List<PortGrant> ports = new();
    List<int> used = new();
    foreach (int port in takenHostPorts)
    {
      used.Add(port);
    }

    foreach (PortGrant grant in current)
    {
      used.Remove(grant.HostPort);
    }

    foreach (PortGrant grant in requested)
    {
      if (grant.ContainerPort < 1 || grant.ContainerPort > MaxPort)
      {
        throw new ArgumentException($"Port {grant.ContainerPort} is not a valid port.");
      }

      foreach (PortGrant existing in ports)
      {
        if (existing.ContainerPort == grant.ContainerPort)
        {
          throw new ArgumentException($"Port {grant.ContainerPort} is granted twice.");
        }
      }

      int hostPort = 0;
      foreach (PortGrant previous in current)
      {
        if (previous.ContainerPort == grant.ContainerPort)
        {
          hostPort = previous.HostPort;
        }
      }

      if (hostPort == 0)
      {
        hostPort = FreeHostPort(options, used);
      }

      used.Add(hostPort);
      ports.Add(new PortGrant(grant.ContainerPort, hostPort));
    }

    return ports;
  }

  private static int FreeHostPort(ComputerOptions options, List<int> used)
  {
    for (int port = options.PortRangeStart; port <= options.PortRangeEnd; port++)
    {
      if (!used.Contains(port))
      {
        return port;
      }
    }

    throw new InvalidOperationException("Every host port in the configured range is already granted.");
  }

  private static List<FolderGrant> ValidateFolders(List<FolderGrant> requested, ComputerOptions options, HostPaths host)
  {
    List<FolderGrant> folders = new();
    foreach (FolderGrant folder in requested)
    {
      ValidateFolderName(folder.Name);
      foreach (FolderGrant existing in folders)
      {
        if (existing.Name == folder.Name)
        {
          throw new ArgumentException($"Two folders are called '{folder.Name}'.");
        }
      }

      string path = NormalizePath(folder.HostPath);
      bool insideRoot = false;
      foreach (string root in options.AllowedFolderRoots)
      {
        insideRoot |= IsInside(path, NormalizePath(root));
      }

      if (!insideRoot)
      {
        throw new ArgumentException($"{path} is not inside a folder Holodeck may share. Allowed roots are set in Computer:AllowedFolderRoots.");
      }

      foreach (string own in new[] { host.ProjectDirectory, host.Data, host.Libraries })
      {
        string ownPath = NormalizePath(own);
        if (IsInside(path, ownPath) || IsInside(ownPath, path))
        {
          throw new ArgumentException($"{path} overlaps Holodeck's own files and cannot be shared with an agent.");
        }
      }

      folders.Add(new FolderGrant(folder.Name, path, folder.ReadOnly));
    }

    return folders;
  }

  private static void ValidateFolderName(string name)
  {
    ReadOnlySpan<char> value = name.AsSpan();
    bool valid = value.Length > 0 && value.Length <= MaxFolderNameLength && value[0] != '-';
    foreach (char character in value)
    {
      valid &= char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '-';
    }

    if (!valid)
    {
      throw new ArgumentException($"Folder name '{name}' must be lowercase letters, digits and dashes, up to {MaxFolderNameLength} characters.");
    }
  }

  // An absolute path without '.', '..' or empty segments; a trailing slash is dropped.
  private static string NormalizePath(string path)
  {
    ReadOnlySpan<char> value = path.AsSpan();
    if (value.Length > 1 && value[^1] == '/')
    {
      value = value[..^1];
    }

    if (value.IsEmpty || value[0] != '/')
    {
      throw new ArgumentException($"'{path}' must be an absolute host path.");
    }

    foreach (Range range in value[1..].Split('/'))
    {
      ReadOnlySpan<char> segment = value[1..][range];
      bool isRelative = segment.IsEmpty || segment.SequenceEqual(".") || segment.SequenceEqual("..");
      if (isRelative && value.Length > 1)
      {
        throw new ArgumentException($"'{path}' must not contain empty, '.' or '..' parts.");
      }
    }

    return value.ToString();
  }

  private static bool IsInside(string path, string folder)
  {
    ReadOnlySpan<char> value = path.AsSpan();
    if (folder == "/")
    {
      return true;
    }

    if (!value.StartsWith(folder, StringComparison.Ordinal))
    {
      return false;
    }

    return value.Length == folder.Length || value[folder.Length] == '/';
  }
}
