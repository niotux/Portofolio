using Npgsql;

namespace Holodeck.Web.Rooms;

public sealed partial class RoomStore
{
  public async Task<RoomMember> ReplaceDefinitionAsync(string projectId, string memberId, LibraryAgent agent)
  {
    RoomMember member = await GetMemberAsync(projectId, memberId);
    RequireAgent(member);
    await using NpgsqlCommand command = database.CreateCommand("UPDATE room_members SET library = $2 WHERE id = $1 AND NOT removed");
    command.Parameters.AddWithValue(memberId);
    command.Parameters.AddWithValue(agent.Folder);
    if (await command.ExecuteNonQueryAsync() == 0)
    {
      throw new InvalidOperationException("The agent has been removed.");
    }

    return member with { Library = agent.Folder };
  }
}
