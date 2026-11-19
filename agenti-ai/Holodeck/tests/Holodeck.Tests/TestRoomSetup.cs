using Holodeck.Web.Projects;
using Holodeck.Web.Rooms;

namespace Holodeck.Tests;

// A project whose room holds You and two placed agents, A and B, with stores over it.
internal sealed class TestRoomSetup
{
  private TestRoomSetup(RoomStore room, PacketStore packets, RoomPaths paths, RoomSignals signals, Project project, RoomMember you, RoomMember a, RoomMember b)
  {
    Room = room;
    Packets = packets;
    Paths = paths;
    Signals = signals;
    Project = project;
    You = you;
    A = a;
    B = b;
  }

  public RoomStore Room { get; }
  public PacketStore Packets { get; }
  public RoomPaths Paths { get; }
  public RoomSignals Signals { get; }
  public Project Project { get; }
  public RoomMember You { get; }
  public RoomMember A { get; }
  public RoomMember B { get; }

  public string TableOf(RoomMember member)
  {
    return Paths.Table(Project.Id, member.Id);
  }

  public static async Task<TestRoomSetup> CreateAsync(TestDatabase database)
  {
    ProjectStore projects = new(database.Database);
    await projects.InitializeAsync(CancellationToken.None);
    RoomPaths paths = TestLibrary.Paths(database.DataPath);
    RoomStore room = new(database.Database, paths);
    await room.InitializeAsync(CancellationToken.None);
    RoomSignals signals = new();
    PacketStore packets = new(database.Database, room, paths, signals);
    Project project = await projects.CreateAsync(new ProjectInput("Packets", null, null), CancellationToken.None);
    TestLibrary.Write(database.DataPath, "agent", "Agent", ["sh", "${Agent}/run.sh"]);
    LibraryAgent agent = TestLibrary.Agent(database.DataPath, "agent");
    RoomMember you = await room.EnsureYouAsync(project.Id);
    RoomMember a = await room.RenameAsync(project.Id, (await room.PlaceAsync(project.Id, agent)).Id, "A");
    RoomMember b = await room.RenameAsync(project.Id, (await room.PlaceAsync(project.Id, agent)).Id, "B");
    return new TestRoomSetup(room, packets, paths, signals, project, you, a, b);
  }
}
