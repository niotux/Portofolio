using Npgsql;

namespace Holodeck.Web.Activity;

public sealed partial class ActivityStore
{
  public async Task<List<ActivityRun>> ReadAsync(string projectId, string channelId)
  {
    List<ActivityRun> result = new();
    Dictionary<string, ActivityRun> byId = new(StringComparer.Ordinal);
    await using NpgsqlConnection connection = await database.OpenConnectionAsync();
    await using (NpgsqlCommand command = new("SELECT a.id,a.member_id,a.packet_id,a.state,a.state='working' AND a.session_hash=m.session_hash AND NOT m.removed AND a.updated_at>clock_timestamp()-make_interval(secs=>$3),a.is_typing,a.updated_at,a.created_at FROM agent_activity a JOIN room_members m ON m.id=a.member_id WHERE a.project_id=$1 AND a.channel_id=$2 ORDER BY a.created_at DESC,a.id DESC LIMIT $4", connection))
    {
      command.Parameters.AddWithValue(projectId);
      command.Parameters.AddWithValue(channelId);
      command.Parameters.AddWithValue(ActivityLimits.LeaseSeconds);
      command.Parameters.AddWithValue(ActivityLimits.RecentRuns);
      await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
      while (await reader.ReadAsync())
      {
        bool active = !reader.IsDBNull(4) && reader.GetBoolean(4);
        string state = reader.GetString(3);
        if (state == "working" && !active)
        {
          state = "interrupted";
        }
        ActivityRun run = new(reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetInt64(2), state, active, active && reader.GetBoolean(5), reader.GetFieldValue<DateTimeOffset>(7), reader.GetFieldValue<DateTimeOffset>(6), new List<ActivityStep>());
        result.Add(run);
        byId.Add(run.Id, run);
      }
    }
    if (result.Count == 0)
    {
      return result;
    }
    string[] ids = new string[result.Count];
    for (int i = 0; i < result.Count; i++)
    {
      ids[i] = result[i].Id;
    }
    await using NpgsqlCommand steps = new("SELECT activity_id,ordinal,phase,label,text,is_complete FROM agent_activity_steps WHERE activity_id=ANY($1) ORDER BY ordinal", connection);
    steps.Parameters.AddWithValue(ids);
    await using NpgsqlDataReader stepReader = await steps.ExecuteReaderAsync();
    while (await stepReader.ReadAsync())
    {
      byId[stepReader.GetString(0)].Steps.Add(new ActivityStep(stepReader.GetInt32(1), stepReader.GetString(2), stepReader.GetString(3), stepReader.GetString(4), stepReader.GetBoolean(5)));
    }
    result.Reverse();
    return result;
  }
}
