using Holodeck.Web.Computers;
using Holodeck.Web.Rooms;
using Microsoft.Extensions.Options;

namespace Holodeck.Web.Endpoints;

// Each agent's computer: what it may use beyond its own disk, its disk use, resetting its installs, and sudo.
public static class ComputerEndpoints
{
  public static void Map(WebApplication app)
  {
    RouteGroupBuilder computer = app.MapGroup("/api/projects/{projectId}/room/agents/{id}/computer");
    computer.MapGet("", ReadAsync);
    computer.MapPut("", WriteAsync);
    computer.MapPost("/reset", ResetAsync);
    computer.MapPut("/sudo", SetSudoAsync);
  }

  private static Task<IResult> ReadAsync(string projectId, string id, RoomStore room, AgentLibrary library, GrantStore grants, ComputerHost host, RoomPaths paths, IOptions<ComputerOptions> options, IAgentRuntime runtime, CancellationToken token)
  {
    return EndpointErrors.RunAsync(async () =>
    {
      RoomMember member = await RequireAgentAsync(room, projectId, id);
      return Results.Ok(await ViewAsync(member, library, grants, paths, options.Value, await runtime.SudoAsync(member, token)));
    });
  }

  private static Task<IResult> SetSudoAsync(string projectId, string id, SudoInput input, RoomStore room, AgentLibrary library, GrantStore grants, RoomPaths paths, IOptions<ComputerOptions> options, IAgentRuntime runtime, AgentSupervisor supervisor, CancellationToken token)
  {
    return EndpointErrors.RunAsync(async () =>
    {
      RoomMember member = await RequireAgentAsync(room, projectId, id);
      await supervisor.SetSudoAsync(member, input.Enabled, token);
      return Results.Ok(await ViewAsync(member, library, grants, paths, options.Value, await runtime.SudoAsync(member, token)));
    });
  }

  private static Task<IResult> WriteAsync(string projectId, string id, ComputerInput input, RoomStore room, AgentLibrary library, GrantStore grants, ComputerHost host, RoomPaths paths, IOptions<ComputerOptions> options, AgentSupervisor supervisor, IAgentRuntime runtime, RoomSignals signals, CancellationToken token)
  {
    return EndpointErrors.RunAsync(async () =>
    {
      RoomMember member = await RequireAgentAsync(room, projectId, id);
      List<PortGrant> ports = new();
      if (input.Ports is not null)
      {
        foreach (int port in input.Ports)
        {
          ports.Add(new PortGrant(port, 0));
        }
      }

      AgentGrants requested = new(ports, input.Folders ?? new());
      AgentGrants current = await grants.ReadAsync(member.Id);
      AgentGrants validated = GrantRules.Validate(requested, current, options.Value, host.Paths, await grants.ListHostPortsAsync());
      if (!SameGrants(current, validated))
      {
        await supervisor.RequireUnprovisionedAsync(member, token);
        await grants.WriteAsync(member.Id, validated);
      }
      signals.Pulse(projectId, member.Id);
      return Results.Ok(await ViewAsync(member, library, grants, paths, options.Value, await runtime.SudoAsync(member, token)));
    });
  }

  private static bool SameGrants(AgentGrants current, AgentGrants requested)
  {
    if (current.Ports.Count != requested.Ports.Count || current.Folders.Count != requested.Folders.Count)
    {
      return false;
    }

    foreach (PortGrant port in current.Ports)
    {
      if (!requested.Ports.Contains(port))
      {
        return false;
      }
    }

    foreach (FolderGrant folder in current.Folders)
    {
      if (!requested.Folders.Contains(folder))
      {
        return false;
      }
    }

    return true;
  }

  private static Task<IResult> ResetAsync(string projectId, string id, RoomStore room, AgentSupervisor supervisor, CancellationToken token)
  {
    return EndpointErrors.RunAsync(async () =>
    {
      RoomMember member = await RequireAgentAsync(room, projectId, id);
      await supervisor.ResetEnvironmentAsync(member, token);
      return Results.NoContent();
    });
  }

  private static async Task<ComputerView> ViewAsync(RoomMember member, AgentLibrary library, GrantStore grants, RoomPaths paths, ComputerOptions options, bool sudo)
  {
    LibraryAgent? definition = DefinitionOf(member, library);
    List<LibraryServiceView> services = new();
    if (definition is not null && definition.Runtime is not null)
    {
      foreach (LibraryEnvironmentProcess process in definition.Runtime.Processes)
      {
        services.Add(new LibraryServiceView(process.Id, definition.Runtime.Image));
      }
    }

    long workspace = DiskUsage.Measure(paths.Table(member.ProjectId, member.Id));
    string home = paths.Home(member.ProjectId, member.Id);
    long homeBytes = DiskUsage.Measure(home);
    bool warning = workspace + homeBytes > options.DiskWarningBytes && options.DiskWarningBytes > 0;
    return new ComputerView(NeedsOf(definition), await grants.ReadAsync(member.Id), services, options.AllowedFolderRoots, workspace, homeBytes, warning, sudo);
  }

  // An unresolved agent still shows its grants; its needs and services return with its library.
  private static LibraryAgent? DefinitionOf(RoomMember member, AgentLibrary library)
  {
    try
    {
      return library.Get(member.Library!);
    }
    catch (KeyNotFoundException)
    {
      return null;
    }
  }

  private static AgentNeeds NeedsOf(LibraryAgent? definition)
  {
    if (definition is null)
    {
      return new AgentNeeds(new List<int>());
    }

    return definition.Needs;
  }

  private static async Task<RoomMember> RequireAgentAsync(RoomStore room, string projectId, string id)
  {
    RoomMember member = await room.GetMemberAsync(projectId, id);
    if (member.IsYou || member.IsRemoved || member.Library is null)
    {
      throw new KeyNotFoundException("That agent is not in this room.");
    }

    return member;
  }
}
