using Holodeck.Web.AgentData;
using Holodeck.Web.Computers;
using Holodeck.Web.Rooms;

namespace Holodeck.Web.Endpoints;

// The whole contract an agent program needs: who am I, give me the next packet, send this.
public static class AgentEndpoints
{
  public static void Map(WebApplication app)
  {
    RouteGroupBuilder agent = app.MapGroup("/api/agent");
    agent.MapGet("/me", (HttpContext context, RoomStore room) => EndpointErrors.RunAsync(async () =>
    {
      AgentCaller caller = await AgentAuthentication.AuthenticateAsync(context, room);
      return Results.Ok(await DescribeAsync(caller.Member, room));
    }));
    agent.MapGet("/next", NextAsync);
    agent.MapPost("/send", SendAsync);
    agent.MapPost("/database", DatabaseAsync);
    agent.MapPost("/library-database/{libraryId}", LibraryDatabaseAsync);
  }

  private static Task<IResult> NextAsync(HttpContext context, int? wait, RoomStore room, PacketStore packets, RoomSignals signals, IAgentRuntime runtime, CancellationToken token)
  {
    return EndpointErrors.RunAsync(async () =>
    {
      AgentCaller caller = await AgentAuthentication.AuthenticateAsync(context, room);
      AgentComputerState? computer = await runtime.InspectAsync(caller.Member, token);
      if (computer is not null)
      {
        await room.ObserveComputerStartAsync(caller.Member.Id, computer.StartedAt);
      }

      int seconds = wait ?? 0;
      if (seconds < 0 || seconds > RoomLimits.MaxWaitSeconds)
      {
        throw new ArgumentException($"Wait between 0 and {RoomLimits.MaxWaitSeconds} seconds.");
      }

      DateTime until = DateTime.UtcNow.AddSeconds(seconds);
      while (true)
      {
        // Register for a wake-up before looking, so a packet sent in between is never missed.
        TimeSpan remaining = until - DateTime.UtcNow;
        Task arrival = signals.WaitForMemberAsync(caller.Member.Id, remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero, token);
        Delivery? delivery = await packets.NextAsync(caller.Member, caller.SessionHash, token);
        if (delivery is not null)
        {
          return Results.Ok(delivery);
        }

        if (remaining <= TimeSpan.Zero)
        {
          return Results.NoContent();
        }

        await arrival;
      }
    });
  }

  private static Task<IResult> SendAsync(HttpContext context, SendInput input, RoomStore room, PacketStore packets, RoomPaths paths, CancellationToken token)
  {
    return EndpointErrors.RunAsync(async () =>
    {
      AgentCaller caller = await AgentAuthentication.AuthenticateAsync(context, room);
      List<OutgoingFile> files = new();
      if (input.Files is not null)
      {
        string table = paths.Table(caller.Member.ProjectId, caller.Member.Id);
        foreach (string path in input.Files)
        {
          files.Add(TablePaths.Resolve(table, path));
        }
      }

      bool hasChannel = !string.IsNullOrEmpty(input.Channel);
      if (!hasChannel && string.IsNullOrEmpty(input.To))
      {
        throw new ArgumentException("Say which channel to send to.");
      }

      string channel = hasChannel ? input.Channel! : (await packets.FindPairChannelAsync(caller.Member, input.To!)).Id;
      SendResult result = await packets.SendAsync(caller.Member, channel, input.Note ?? "", files, input.IdempotencyKey, token);
      if (result.IsReplay)
      {
        return Results.Ok(result.Packet);
      }

      return Results.Created($"/api/agent/packets/{result.Packet.Id}", result.Packet);
    });
  }

  // The agent's own database: its SQL runs as its own role in its own schema, one transaction per call.
  private static Task<IResult> DatabaseAsync(HttpContext context, SqlRequest input, RoomStore room, AgentDatabases databases, CancellationToken token)
  {
    return EndpointErrors.RunAsync(async () =>
    {
      AgentCaller caller = await AgentAuthentication.AuthenticateAsync(context, room);
      return Results.Ok(await databases.RunAsync(caller.Member, input, false, token));
    });
  }

  private static Task<IResult> LibraryDatabaseAsync(HttpContext context, string libraryId, SqlRequest input, RoomStore room, AgentLibrary libraries, AgentDatabases databases, CancellationToken token)
  {
    return EndpointErrors.RunAsync(async () =>
    {
      AgentCaller caller = await AgentAuthentication.AuthenticateAsync(context, room);
      if (caller.Member.Library is null)
      {
        throw new UnauthorizedAccessException("Only a library agent can use a shared library database.");
      }

      LibraryAgent agent = libraries.Get(caller.Member.Library);
      string key = DatabaseLibraryKey(agent, libraryId);
      return Results.Ok(await databases.RunLibraryAsync(key, input, false, token));
    });
  }

  private static string DatabaseLibraryKey(LibraryAgent agent, string libraryId)
  {
    LibraryFiles.RequireId(libraryId, "Library Id");
    int separator = agent.LibraryKey.IndexOf('/');
    if (separator > 0 && agent.LibraryKey[..separator] == libraryId)
    {
      return agent.LibraryKey;
    }

    if (agent.Dependencies is not null)
    {
      foreach (LibraryMount dependency in agent.Dependencies)
      {
        if (dependency.Id == libraryId)
        {
          return dependency.Id + "/" + dependency.Version;
        }
      }
    }

    throw new UnauthorizedAccessException("This agent can use only its own library database or a directly declared dependency's database.");
  }

  private static async Task<AgentIdentity> DescribeAsync(RoomMember member, RoomStore room)
  {
    List<RoomMember> members = await room.ListMembersAsync(member.ProjectId);
    List<string> peers = new();
    List<AgentChannel> channels = new();
    foreach (ChannelSummary channel in await room.ListChannelsAsync(member.ProjectId))
    {
      if (!channel.MemberIds.Contains(member.Id))
      {
        continue;
      }

      List<string> names = new();
      List<AgentParticipant> participants = new();
      foreach (RoomMember other in members)
      {
        if (other.IsRemoved || !channel.MemberIds.Contains(other.Id))
        {
          continue;
        }

        participants.Add(new AgentParticipant(other.Id, other.Name, other.IsYou));
        if (other.Id == member.Id)
        {
          continue;
        }

        names.Add(other.Name);
        if (!peers.Contains(other.Name))
        {
          peers.Add(other.Name);
        }
      }

      channels.Add(new AgentChannel(channel.Id, channel.Name, names, participants));
    }

    peers.Sort(StringComparer.OrdinalIgnoreCase);
    return new AgentIdentity(member.Name, member.ProjectId, peers, channels, member.Id, [AgentCapabilities.IdempotentSend, AgentCapabilities.ManagementJobs, AgentCapabilities.Database, AgentCapabilities.LibraryDatabase]);
  }
}
