namespace Holodeck.Web.Rooms;

public sealed partial class AgentLibrary
{
  private static void ReadTeams(LibraryPackage package, Dictionary<string, LibraryPackage> packages, List<LibraryDiagnostic> diagnostics)
  {
    try
    {
      string directory = LibraryFiles.Inside(package.Directory, "teams");
      if (!Directory.Exists(directory))
      {
        throw new ArgumentException("A library must contain a teams directory.");
      }

      HashSet<string> ids = new(StringComparer.Ordinal);
      foreach (string teamDirectory in Directory.GetDirectories(directory))
      {
        string relative = Path.GetRelativePath(package.Directory, teamDirectory);
        TeamManifest manifest = LibraryFiles.Read<TeamManifest>(LibraryFiles.Inside(package.Directory, relative + "/Team.json"));
        string id = LibraryFiles.RequireId(manifest.Id, "Team Id");
        if (!ids.Add(id))
        {
          throw new ArgumentException($"Duplicate team Id '{id}'.");
        }

        string name = AgentNames.Validate(manifest.Name ?? "");
        List<LibraryTeamMember> members = ResolveMembers(package, manifest, packages);
        List<List<string>> wires = manifest.Wires ?? new();
        ValidateWires(members, wires);
        List<LibraryTeamChannel> channels = ReadChannels(members, manifest.Channels);
        package.Teams.Add(new LibraryTeam($"{package.Key}/teams/{id}", name, manifest.Description ?? "", members, wires, channels));
      }
    }
    catch (Exception exception) when (LibraryFiles.IsManifestError(exception))
    {
      Reject(package, exception.Message, diagnostics);
    }
  }

  private static List<LibraryTeamMember> ResolveMembers(LibraryPackage package, TeamManifest manifest, Dictionary<string, LibraryPackage> packages)
  {
    if (manifest.Members is null)
    {
      throw new ArgumentException("A team must contain Members.");
    }

    if (manifest.Members.Count == 0)
    {
      throw new ArgumentException("A team must contain Members.");
    }

    List<LibraryTeamMember> members = new();
    HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase) { "You" };
    foreach (TeamMemberManifest member in manifest.Members)
    {
      if (member is null)
      {
        throw new ArgumentException("Team Members must contain member objects.");
      }

      string id = LibraryFiles.RequireId(member.Id, "Team member Id");
      if (!ids.Add(id))
      {
        throw new ArgumentException($"Duplicate or reserved team member Id '{id}'.");
      }

      string name = AgentNames.Validate(member.Name ?? "");
      LibraryAgent agent = ResolveAgent(package, member.Agent ?? "", packages);
      members.Add(new LibraryTeamMember(id, name, agent));
    }

    return members;
  }

  private static LibraryAgent ResolveAgent(LibraryPackage package, string reference, Dictionary<string, LibraryPackage> packages)
  {
    string[] parts = reference.Split('/');
    if (parts.Length != 3 || parts[1] != "agents")
    {
      throw new ArgumentException($"Agent reference '{reference}' must be libraryId/agents/agentId.");
    }

    LibraryPackage target = package;
    if (parts[0] != package.Manifest.Id)
    {
      string? dependencyKey = null;
      if (package.Manifest.Dependencies is not null)
      {
        foreach (LibraryDependency dependency in package.Manifest.Dependencies)
        {
          if (dependency.Id == parts[0])
          {
            dependencyKey = $"{dependency.Id}/{dependency.Version}";
          }
        }
      }

      if (dependencyKey is null || !packages.TryGetValue(dependencyKey, out LibraryPackage? dependencyPackage))
      {
        throw new ArgumentException($"Agent reference '{reference}' requires a declared local library dependency.");
      }

      target = dependencyPackage;
    }

    foreach (LibraryAgent agent in target.Agents)
    {
      if (agent.Folder == $"{target.Key}/agents/{parts[2]}")
      {
        return agent;
      }
    }

    throw new ArgumentException($"Agent reference '{reference}' does not exist.");
  }

  private static List<LibraryTeamChannel> ReadChannels(List<LibraryTeamMember> members, List<TeamChannelManifest>? declared)
  {
    List<LibraryTeamChannel> channels = new();
    if (declared is null)
    {
      return channels;
    }

    List<string> ids = new() { "You" };
    foreach (LibraryTeamMember member in members)
    {
      ids.Add(member.Id);
    }

    List<string> names = new();
    foreach (TeamChannelManifest channel in declared)
    {
      if (channel is null)
      {
        throw new ArgumentException("Each team channel needs a Name and Members.");
      }

      string name = AgentNames.Validate(channel.Name ?? "");
      foreach (string taken in names)
      {
        if (string.Equals(taken, name, StringComparison.OrdinalIgnoreCase))
        {
          throw new ArgumentException($"The team channel '{name}' is declared more than once.");
        }
      }

      names.Add(name);
      List<string> channelMembers = new();
      if (channel.Members is not null)
      {
        foreach (string memberId in channel.Members)
        {
          if (!ids.Contains(memberId) || channelMembers.Contains(memberId))
          {
            throw new ArgumentException($"Team channel '{name}' lists an unknown or repeated member.");
          }

          channelMembers.Add(memberId);
        }
      }

      if (channelMembers.Count < 2)
      {
        throw new ArgumentException($"Team channel '{name}' needs at least two members.");
      }

      channels.Add(new LibraryTeamChannel(name, channelMembers));
    }

    return channels;
  }

  private static void ValidateWires(List<LibraryTeamMember> members, List<List<string>> wires)
  {
    HashSet<string> ids = new(StringComparer.Ordinal) { "You" };
    foreach (LibraryTeamMember member in members)
    {
      ids.Add(member.Id);
    }

    HashSet<string> pairs = new(StringComparer.Ordinal);
    foreach (List<string> wire in wires)
    {
      if (wire is null)
      {
        throw new ArgumentException("Each team wire must contain exactly two member IDs.");
      }

      if (wire.Count != 2)
      {
        throw new ArgumentException("Each team wire must contain exactly two member IDs.");
      }

      if (!ids.Contains(wire[0]) || !ids.Contains(wire[1]) || wire[0] == wire[1])
      {
        throw new ArgumentException("Team wires must connect two distinct existing members or You.");
      }

      string pair = string.CompareOrdinal(wire[0], wire[1]) < 0 ? wire[0] + "/" + wire[1] : wire[1] + "/" + wire[0];
      if (!pairs.Add(pair))
      {
        throw new ArgumentException("A team wire is declared more than once.");
      }
    }
  }
}
