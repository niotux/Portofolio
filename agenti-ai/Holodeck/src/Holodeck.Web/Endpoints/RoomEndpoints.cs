using System.Text;
using Holodeck.Web.AgentData;
using Holodeck.Web.Computers;
using Holodeck.Web.Projects;
using Holodeck.Web.Rooms;
using Holodeck.Web.Settings;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Options;

namespace Holodeck.Web.Endpoints;

// What the room window uses: members, channels, conversations, and a live stream of changes. Everything is
// done as You; there is no way to speak as another member.
public static class RoomEndpoints
{
  private const string PlainText = "text/plain; charset=utf-8";
  private const string DefaultMediaType = "application/octet-stream";
  private static readonly FileExtensionContentTypeProvider MediaTypes = new();

  public static void Map(WebApplication app)
  {
    app.MapGet("/api/library/catalog", (AgentLibrary library) => Results.Ok(CatalogView(library)));
    RouteGroupBuilder room = app.MapGroup("/api/projects/{projectId}/room");
    room.MapGet("", ViewAsync);
    room.MapPost("/agents", PlaceAsync);
    room.MapPost("/teams", PlaceTeamAsync);
    room.MapPost("/agents/{id}/definition", ReplaceDefinitionAsync);
    room.MapDelete("/unresolved", RemoveUnresolvedAsync);
    room.MapPut("/agents/{id}", RenameAsync);
    room.MapDelete("/agents/{id}", RemoveAsync);
    room.MapPost("/agents/{id}/start", StartAsync);
    room.MapPost("/agents/{id}/stop", StopAsync);
    room.MapGet("/agents/{id}/settings", ReadSettingsAsync);
    room.MapPut("/agents/{id}/settings", WriteSettingsAsync);
    room.MapGet("/agents/{id}/settings/form", ReadSettingsFormAsync);
    room.MapPut("/agents/{id}/settings/form", WriteSettingsFormAsync);
    room.MapGet("/agents/{id}/log", ReadLogAsync);
    room.MapGet("/agents/{id}/table", ListTableAsync);
    room.MapGet("/agents/{id}/table/file", ReadTableFileAsync);
    room.MapPost("/agents/{id}/database", QueryDatabaseAsync);
    room.MapDelete("/agents/{id}/database", EraseDatabaseAsync);
    room.MapPost("/channels", CreateChannelAsync);
    room.MapPut("/channels/{id}", UpdateChannelAsync);
    room.MapDelete("/channels/{id}", DeleteChannelAsync);
    room.MapPost("/channels/{id}/members", AddChannelMemberAsync);
    room.MapDelete("/channels/{id}/members/{memberId}", RemoveChannelMemberAsync);
    room.MapGet("/channels/{id}/packets", HistoryAsync);
    room.MapPost("/channels/{id}/packets", SendAsYouAsync).DisableAntiforgery();
    room.MapGet("/packets/{packetId:long}/files/{name}", ReadPacketFile);
  }

  private static Task<IResult> ViewAsync(string projectId, RoomStore room, AgentSupervisor supervisor, ProjectLibraryStore projectLibraries)
  {
    return EndpointErrors.RunAsync(async () =>
    {
      await room.EnsureYouAsync(projectId);
      List<RoomMemberView> members = new();
      foreach (RoomMember member in await room.ListMembersAsync(projectId))
      {
        members.Add(await MemberViewAsync(member, supervisor, projectLibraries));
      }

      return Results.Ok(new RoomView(members, await room.ListChannelsAsync(projectId)));
    });
  }

  private static Task<IResult> PlaceAsync(string projectId, PlaceInput input, RoomStore room, AgentLibrary library, AgentSupervisor supervisor, RoomSignals signals, ProjectLibraryStore projectLibraries, GrantStore grants, ComputerHost host, IOptions<ComputerOptions> options)
  {
    return EndpointErrors.RunAsync(async () =>
    {
      LibraryAgent agent = library.Get(input.Library ?? "");
      await projectLibraries.RequireAgentAsync(projectId, agent);
      AgentGrants configured = await PlacementGrantsAsync(input.Computer, grants, host, options.Value);
      RoomMember member = await room.PlaceAsync(projectId, agent);
      await grants.WriteAsync(member.Id, configured);
      await supervisor.CreateAgentAsync(member, CancellationToken.None);
      signals.Pulse(projectId, member.Id);
      return Results.Created($"/api/projects/{projectId}/room/agents/{member.Id}", await MemberViewAsync(member, supervisor, projectLibraries));
    });
  }

  private static async Task<AgentGrants> PlacementGrantsAsync(ComputerInput? input, GrantStore grants, ComputerHost host, ComputerOptions options)
  {
    AgentGrants empty = new(new List<PortGrant>(), new List<FolderGrant>());
    if (input is null)
    {
      return empty;
    }

    List<PortGrant> ports = new();
    if (input.Ports is not null)
    {
      foreach (int port in input.Ports)
      {
        ports.Add(new PortGrant(port, 0));
      }
    }

    AgentGrants requested = new(ports, input.Folders ?? new List<FolderGrant>());
    return GrantRules.Validate(requested, empty, options, host.Paths, await grants.ListHostPortsAsync());
  }

  private static Task<IResult> PlaceTeamAsync(string projectId, PlaceTeamInput input, RoomStore room, AgentLibrary library, AgentSupervisor supervisor, RoomSignals signals, ProjectLibraryStore projectLibraries)
  {
    return EndpointErrors.RunAsync(async () =>
    {
      LibraryTeam team = library.GetTeam(input.Team ?? "");
      await projectLibraries.RequireTeamAsync(projectId, team);
      List<RoomMember> members = await room.PlaceTeamAsync(projectId, team);
      List<RoomMemberView> views = new();
      foreach (RoomMember member in members)
      {
        await supervisor.CreateAgentAsync(member, CancellationToken.None);
        views.Add(await MemberViewAsync(member, supervisor, projectLibraries));
        signals.Pulse(projectId, member.Id);
      }

      return Results.Created($"/api/projects/{projectId}/room", views);
    });
  }

  private static Task<IResult> ReplaceDefinitionAsync(string projectId, string id, ReplaceDefinitionInput input, RoomStore room, AgentLibrary library, AgentSupervisor supervisor, ProjectLibraryStore projectLibraries, RoomSignals signals, CancellationToken token)
  {
    return EndpointErrors.RunAsync(async () =>
    {
      RoomMember member = await room.GetMemberAsync(projectId, id);
      LibraryAgent agent = library.Get(input.Library ?? "");
      await projectLibraries.RequireAgentAsync(projectId, agent);
      if (member.Library != agent.Folder)
      {
        await supervisor.RequireUnprovisionedAsync(member, token);
      }

      await supervisor.StopAgentAsync(member, token);
      member = await room.ReplaceDefinitionAsync(projectId, id, agent);
      await supervisor.CreateAgentAsync(member, token);
      signals.Pulse(projectId, id);
      return Results.Ok(await MemberViewAsync(member, supervisor, projectLibraries));
    });
  }

  private static Task<IResult> RemoveUnresolvedAsync(string projectId, RoomStore room, AgentSupervisor supervisor, ProjectLibraryStore projectLibraries, RoomSignals signals, CancellationToken token)
  {
    return EndpointErrors.RunAsync(async () =>
    {
      await room.EnsureYouAsync(projectId);
      foreach (RoomMember member in await room.ListMembersAsync(projectId))
      {
        if (member.IsYou || member.IsRemoved)
        {
          continue;
        }

        if (await projectLibraries.ErrorForAsync(projectId, member.Library) is null)
        {
          continue;
        }

        await supervisor.DeleteAgentAsync(member, token);
        await room.RemoveAsync(projectId, member.Id);
        signals.Pulse(projectId, member.Id);
      }

      return Results.NoContent();
    });
  }

  private static Task<IResult> RenameAsync(string projectId, string id, RenameInput input, RoomStore room, AgentSupervisor supervisor, RoomSignals signals, ProjectLibraryStore projectLibraries)
  {
    return EndpointErrors.RunAsync(async () =>
    {
      RoomMember member = await room.RenameAsync(projectId, id, input.Name);
      signals.Pulse(projectId, member.Id);
      return Results.Ok(await MemberViewAsync(member, supervisor, projectLibraries));
    });
  }

  private static Task<IResult> RemoveAsync(string projectId, string id, RoomStore room, AgentSupervisor supervisor, RoomSignals signals, CancellationToken token)
  {
    return EndpointErrors.RunAsync(async () =>
    {
      RoomMember member = await room.GetMemberAsync(projectId, id);
      if (!member.IsYou && !member.IsRemoved)
      {
        await supervisor.DeleteAgentAsync(member, token);
      }

      await room.RemoveAsync(projectId, id);
      signals.Pulse(projectId, id);
      return Results.NoContent();
    });
  }

  private static Task<IResult> StartAsync(string projectId, string id, RoomStore room, AgentSupervisor supervisor, ProjectLibraryStore projectLibraries, CancellationToken token)
  {
    return EndpointErrors.RunAsync(async () =>
    {
      RoomMember member = await room.GetMemberAsync(projectId, id);
      await supervisor.StartAgentAsync(member, token);
      return Results.Ok(await MemberViewAsync(member, supervisor, projectLibraries));
    });
  }

  private static Task<IResult> StopAsync(string projectId, string id, RoomStore room, AgentSupervisor supervisor, ProjectLibraryStore projectLibraries, CancellationToken token)
  {
    return EndpointErrors.RunAsync(async () =>
    {
      RoomMember member = await room.GetMemberAsync(projectId, id);
      await supervisor.StopAgentAsync(member, token);
      return Results.Ok(await MemberViewAsync(member, supervisor, projectLibraries));
    });
  }

  private static Task<IResult> ReadSettingsAsync(string projectId, string id, RoomStore room, RoomPaths paths)
  {
    return EndpointErrors.RunAsync(async () =>
    {
      RoomMember member = await RequireAgentAsync(room, projectId, id);
      string file = paths.SettingsFile(projectId, member.Id);
      string settings = File.Exists(file) ? await File.ReadAllTextAsync(file) : "";
      return Results.Text(settings, PlainText);
    });
  }

  private static Task<IResult> WriteSettingsAsync(string projectId, string id, HttpRequest request, RoomStore room, AgentSettingsStore settingsStore, CancellationToken token)
  {
    return EndpointErrors.RunAsync(async () =>
    {
      RoomMember member = await RequireAgentAsync(room, projectId, id);
      bool isTooLarge = request.ContentLength is not null && request.ContentLength.Value > RoomLimits.MaxSettingsBytes;
      using StreamReader reader = new(request.Body, Encoding.UTF8);
      string settings = isTooLarge ? "" : await reader.ReadToEndAsync(token);
      if (isTooLarge || Encoding.UTF8.GetByteCount(settings) > RoomLimits.MaxSettingsBytes)
      {
        throw new ArgumentException($"Keep settings under {RoomLimits.MaxSettingsBytes / 1024} KB.");
      }

      SettingsWriteResult result = await settingsStore.ReplaceAsync(member, settings);
      if (result.Status == SettingsWriteStatus.Invalid)
      {
        return Results.BadRequest(new SettingsRejection(result.Message, result.Errors));
      }

      return Results.NoContent();
    });
  }

  // The form editor: settings with their schema and revisions, saved only against the revision they were read at.
  private static Task<IResult> ReadSettingsFormAsync(string projectId, string id, RoomStore room, AgentSettingsStore settingsStore)
  {
    return EndpointErrors.RunAsync(async () =>
    {
      RoomMember member = await RequireAgentAsync(room, projectId, id);
      return Results.Ok(await settingsStore.ReadAsync(member));
    });
  }

  private static Task<IResult> WriteSettingsFormAsync(string projectId, string id, SettingsWrite input, RoomStore room, AgentSettingsStore settingsStore)
  {
    return EndpointErrors.RunAsync(async () =>
    {
      RoomMember member = await RequireAgentAsync(room, projectId, id);
      SettingsWriteResult result = await settingsStore.WriteAsync(member, input);
      if (result.Status == SettingsWriteStatus.Conflict)
      {
        return Results.Conflict(new SettingsConflict(result.Message, result.View));
      }

      if (result.Status == SettingsWriteStatus.Invalid)
      {
        return Results.BadRequest(new SettingsRejection(result.Message, result.Errors));
      }

      return Results.Ok(result.View);
    });
  }

  private static Task<IResult> ReadLogAsync(string projectId, string id, RoomStore room, AgentSupervisor supervisor)
  {
    return EndpointErrors.RunAsync(async () =>
    {
      RoomMember member = await RequireAgentAsync(room, projectId, id);
      return Results.Text(supervisor.ReadLog(member), PlainText);
    });
  }

  private static Task<IResult> ListTableAsync(string projectId, string id, RoomStore room, RoomPaths paths)
  {
    return EndpointErrors.RunAsync(async () =>
    {
      RoomMember member = await RequireAgentAsync(room, projectId, id);
      string table = paths.Table(projectId, member.Id);
      List<TableEntry> entries = new();
      if (!Directory.Exists(table))
      {
        return Results.Ok(entries);
      }

      string[] files = Directory.GetFiles(table, "*", SearchOption.AllDirectories);
      Array.Sort(files, StringComparer.Ordinal);
      foreach (string file in files)
      {
        if (entries.Count == RoomLimits.MaxTableListing)
        {
          break;
        }

        entries.Add(new TableEntry(Path.GetRelativePath(table, file), new FileInfo(file).Length));
      }

      return Results.Ok(entries);
    });
  }

  private static Task<IResult> ReadTableFileAsync(string projectId, string id, string? path, RoomStore room, RoomPaths paths)
  {
    return EndpointErrors.RunAsync(async () =>
    {
      RoomMember member = await RequireAgentAsync(room, projectId, id);
      OutgoingFile file = TablePaths.Resolve(paths.Table(projectId, member.Id), path ?? "");
      return Results.File(TableFiles.OpenRead(file.Table!, file.SourcePath, file.Name), MediaTypeOf(file.Name), file.Name);
    });
  }

  private static Task<IResult> CreateChannelAsync(string projectId, CreateChannelInput input, RoomStore room, RoomSignals signals)
  {
    return EndpointErrors.RunAsync(async () =>
    {
      Channel channel = await room.CreateChannelAsync(projectId, input.Name ?? "", input.MemberIds ?? new());
      signals.Pulse(projectId, "");
      return Results.Created($"/api/projects/{projectId}/room/channels/{channel.Id}", channel);
    });
  }

  private static Task<IResult> UpdateChannelAsync(string projectId, string id, ChannelInput input, RoomStore room, RoomSignals signals)
  {
    return EndpointErrors.RunAsync(async () =>
    {
      Channel channel = await room.GetChannelAsync(projectId, id);
      if (input.Name is not null)
      {
        channel = await room.RenameChannelAsync(projectId, channel.Id, input.Name);
      }

      if (input.IsPaused is not null)
      {
        channel = await room.SetChannelPausedAsync(projectId, channel.Id, input.IsPaused.Value);
      }

      signals.Pulse(projectId, "");
      return Results.Ok(channel);
    });
  }

  private static Task<IResult> DeleteChannelAsync(string projectId, string id, RoomStore room, RoomSignals signals)
  {
    return EndpointErrors.RunAsync(async () =>
    {
      await room.DeleteChannelAsync(projectId, id);
      signals.Pulse(projectId, "");
      return Results.NoContent();
    });
  }

  private static Task<IResult> AddChannelMemberAsync(string projectId, string id, ChannelMemberInput input, RoomStore room, RoomSignals signals)
  {
    return EndpointErrors.RunAsync(async () =>
    {
      Channel channel = await room.AddChannelMemberAsync(projectId, id, input.MemberId ?? "");
      signals.Pulse(projectId, "");
      return Results.Ok(channel);
    });
  }

  private static Task<IResult> RemoveChannelMemberAsync(string projectId, string id, string memberId, RoomStore room, RoomSignals signals)
  {
    return EndpointErrors.RunAsync(async () =>
    {
      Channel channel = await room.RemoveChannelMemberAsync(projectId, id, memberId);
      signals.Pulse(projectId, "");
      return Results.Ok(channel);
    });
  }

  private static Task<IResult> HistoryAsync(string projectId, string id, long? after, RoomStore room, PacketStore packets, CancellationToken token)
  {
    return EndpointErrors.RunAsync(async () =>
    {
      Channel channel = await room.GetChannelAsync(projectId, id);
      return Results.Ok(await packets.HistoryAsync(projectId, channel.Id, after ?? 0, token));
    });
  }

  private static Task<IResult> SendAsYouAsync(string projectId, string id, HttpRequest request, RoomStore room, PacketStore packets, CancellationToken token)
  {
    return EndpointErrors.RunAsync(async () =>
    {
      if (!request.HasFormContentType)
      {
        throw new ArgumentException("Send a form with a note and files.");
      }

      IFormCollection form = await request.ReadFormAsync(token);
      Channel channel = await room.GetChannelAsync(projectId, id);
      RoomMember you = await room.EnsureYouAsync(projectId);
      string uploads = Path.Combine(Path.GetTempPath(), "holodeck-upload-" + Guid.NewGuid().ToString("N"));
      try
      {
        List<OutgoingFile> files = await SaveUploadsAsync(form.Files, uploads, token);
        Packet packet = await packets.SendAsync(you, channel.Id, form["note"].ToString(), files, token);
        return Results.Created($"/api/projects/{projectId}/room/channels/{channel.Id}/packets", packet);
      }
      finally
      {
        if (Directory.Exists(uploads))
        {
          Directory.Delete(uploads, true);
        }
      }
    });
  }

  private static IResult ReadPacketFile(string projectId, long packetId, string name, PacketStore packets)
  {
    try
    {
      return Results.File(packets.FilePath(projectId, packetId, name), MediaTypeOf(name), name);
    }
    catch (KeyNotFoundException exception)
    {
      return Results.NotFound(new ErrorResponse(exception.Message));
    }
  }

  private static async Task<List<OutgoingFile>> SaveUploadsAsync(IFormFileCollection uploads, string directory, CancellationToken token)
  {
    List<OutgoingFile> files = new();
    for (int index = 0; index < uploads.Count; index++)
    {
      IFormFile upload = uploads[index];
      string name = Path.GetFileName(upload.FileName);
      if (name.Length == 0 || upload.Length > RoomLimits.MaxFileBytes)
      {
        throw new ArgumentException($"Attach files up to {RoomLimits.MaxFileBytes / (1024 * 1024)} MB with a name.");
      }

      // Each upload gets its own folder so two files with one name do not overwrite each other.
      string folder = Path.Combine(directory, index.ToString());
      Directory.CreateDirectory(folder);
      string path = Path.Combine(folder, name);
      await using (FileStream target = File.Create(path))
      {
        await upload.CopyToAsync(target, token);
      }

      files.Add(new OutgoingFile(name, path));
    }

    return files;
  }

  // Look into an agent's own database, read-only, as that agent's role.
  private static Task<IResult> QueryDatabaseAsync(string projectId, string id, SqlRequest input, RoomStore room, AgentDatabases databases, CancellationToken token)
  {
    return EndpointErrors.RunAsync(async () =>
    {
      RoomMember member = await RequireAgentAsync(room, projectId, id);
      return Results.Ok(await databases.RunAsync(member, input, true, token));
    });
  }

  // Wipes everything the agent kept in its database; it starts with an empty one next time.
  private static Task<IResult> EraseDatabaseAsync(string projectId, string id, RoomStore room, AgentSupervisor supervisor, AgentDatabases databases)
  {
    return EndpointErrors.RunAsync(async () =>
    {
      RoomMember member = await RequireAgentAsync(room, projectId, id);
      if (supervisor.Status(member.Id) == AgentStatus.Running)
      {
        throw new InvalidOperationException("Stop the agent before erasing its database.");
      }

      await databases.DropAsync(member.Id);
      return Results.NoContent();
    });
  }

  private static async Task<RoomMember> RequireAgentAsync(RoomStore room, string projectId, string id)
  {
    RoomMember member = await room.GetMemberAsync(projectId, id);
    if (member.IsYou)
    {
      throw new InvalidOperationException("You have no settings, log, or table.");
    }

    return member;
  }

  private static async Task<RoomMemberView> MemberViewAsync(RoomMember member, AgentSupervisor supervisor, ProjectLibraryStore projectLibraries)
  {
    string? error = null;
    if (!member.IsYou && !member.IsRemoved)
    {
      error = await projectLibraries.ErrorForAsync(member.ProjectId, member.Library);
    }

    string status = error is null ? StatusOf(member, supervisor) : "error";
    return new RoomMemberView(member.Id, member.Name, member.IsYou, member.Library, member.IsRemoved, status, error);
  }

  private static string StatusOf(RoomMember member, AgentSupervisor supervisor)
  {
    if (member.IsYou)
    {
      return "you";
    }

    if (member.IsRemoved)
    {
      return "removed";
    }

    AgentStatus status = supervisor.Status(member.Id);
    if (status == AgentStatus.Running)
    {
      return "running";
    }

    if (status == AgentStatus.Crashed)
    {
      return "crashed";
    }

    return "stopped";
  }

  private static LibraryCatalogView CatalogView(AgentLibrary library)
  {
    LibraryCatalog catalog = library.ReadCatalog();
    List<LibraryAgentView> agents = new();
    List<LibraryTeamView> teams = new();
    foreach (LibraryAgent agent in catalog.Agents)
    {
      agents.Add(new LibraryAgentView(agent.Folder, agent.Name, agent.Description));
    }

    foreach (LibraryTeam team in catalog.Teams)
    {
      teams.Add(new LibraryTeamView(team.Folder, team.Name, team.Description, team.Members.Count));
    }

    List<LibraryCatalogAppletView> applets = new();
    foreach (LibraryApplicationPackage package in catalog.Applications)
    {
      foreach (LibraryApplet applet in package.Applets)
      {
        applets.Add(new LibraryCatalogAppletView(package.Key + "/" + applet.EntryPoint, applet.Id, applet.Name, applet.Description ?? "", applet.Icon ?? LibraryApplet.DefaultIcon, applet.Services ?? new()));
      }
    }

    return new LibraryCatalogView(agents, teams, applets, catalog.Diagnostics, catalog.Libraries);
  }

  private static string MediaTypeOf(string name)
  {
    if (MediaTypes.TryGetContentType(name, out string? mediaType))
    {
      return mediaType;
    }

    return DefaultMediaType;
  }
}
