using Holodeck.Web.Rooms;
using Npgsql;
using NpgsqlTypes;

namespace Holodeck.Web.Activity;

// Generic live work/prose. Holodeck has no knowledge of which mental mechanism an agent runs.
public sealed partial class ActivityStore(NpgsqlDataSource database)
{
  public async Task InitializeAsync()
  {
    const string Sql = """
      CREATE TABLE IF NOT EXISTS agent_activity (
        id text PRIMARY KEY,
        project_id text NOT NULL REFERENCES memory_projects(id) ON DELETE CASCADE,
        member_id text NOT NULL REFERENCES room_members(id) ON DELETE CASCADE,
        channel_id text NOT NULL REFERENCES room_channels(id) ON DELETE CASCADE,
        packet_id bigint REFERENCES room_packets(id) ON DELETE CASCADE,
        session_hash text NOT NULL,
        state text NOT NULL,
        is_typing bool NOT NULL,
        updated_at timestamptz NOT NULL DEFAULT now(),
        created_at timestamptz NOT NULL DEFAULT now());
      CREATE INDEX IF NOT EXISTS agent_activity_channel ON agent_activity(channel_id,created_at);
      CREATE TABLE IF NOT EXISTS agent_activity_steps (
        activity_id text NOT NULL REFERENCES agent_activity(id) ON DELETE CASCADE,
        ordinal int NOT NULL,
        phase text NOT NULL,
        label text NOT NULL,
        text text NOT NULL,
        is_complete bool NOT NULL,
        PRIMARY KEY(activity_id,ordinal));
      """;
    await using NpgsqlCommand command = database.CreateCommand(Sql);
    await command.ExecuteNonQueryAsync();
  }

  public async Task WriteAsync(AgentCaller caller, ActivityUpdate input)
  {
    Validate(input);
    await using NpgsqlConnection connection = await database.OpenConnectionAsync();
    await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
    await AuthorizeAsync(connection, transaction, caller, input);
    await using (NpgsqlCommand insert = new("INSERT INTO agent_activity(id,project_id,member_id,channel_id,packet_id,session_hash,state,is_typing) VALUES($1,$2,$3,$4,$5,$6,'working',false) ON CONFLICT(id) DO NOTHING", connection, transaction))
    {
      insert.Parameters.AddWithValue(input.Id);
      insert.Parameters.AddWithValue(caller.Member.ProjectId);
      insert.Parameters.AddWithValue(caller.Member.Id);
      insert.Parameters.AddWithValue(input.ChannelId);
      insert.Parameters.AddWithValue(NpgsqlDbType.Bigint, (object?)input.PacketId ?? DBNull.Value);
      insert.Parameters.AddWithValue(caller.SessionHash);
      await insert.ExecuteNonQueryAsync();
    }
    await using (NpgsqlCommand update = new("UPDATE agent_activity SET state=$7,is_typing=$8,updated_at=clock_timestamp() WHERE id=$1 AND project_id=$2 AND member_id=$3 AND channel_id=$4 AND packet_id IS NOT DISTINCT FROM $5 AND session_hash=$6 AND state='working'", connection, transaction))
    {
      update.Parameters.AddWithValue(input.Id);
      update.Parameters.AddWithValue(caller.Member.ProjectId);
      update.Parameters.AddWithValue(caller.Member.Id);
      update.Parameters.AddWithValue(input.ChannelId);
      update.Parameters.AddWithValue(NpgsqlDbType.Bigint, (object?)input.PacketId ?? DBNull.Value);
      update.Parameters.AddWithValue(caller.SessionHash);
      update.Parameters.AddWithValue(input.State);
      update.Parameters.AddWithValue(input.IsTyping && input.State == "working");
      if (await update.ExecuteNonQueryAsync() != 1)
      {
        throw new InvalidOperationException("That activity is complete or belongs to a different run.");
      }
    }
    await using (NpgsqlCommand step = new("INSERT INTO agent_activity_steps(activity_id,ordinal,phase,label,text,is_complete) VALUES($1,$2,$3,$4,$5,$6) ON CONFLICT(activity_id,ordinal) DO UPDATE SET label=$4,text=$5,is_complete=agent_activity_steps.is_complete OR $6 WHERE agent_activity_steps.phase=$3", connection, transaction))
    {
      step.Parameters.AddWithValue(input.Id);
      step.Parameters.AddWithValue(input.Ordinal);
      step.Parameters.AddWithValue(input.Phase);
      step.Parameters.AddWithValue(input.Label);
      step.Parameters.AddWithValue(input.Text);
      step.Parameters.AddWithValue(input.IsComplete);
      if (await step.ExecuteNonQueryAsync() != 1)
      {
        throw new InvalidOperationException("An activity phase cannot change its identity.");
      }
    }
    await transaction.CommitAsync();
  }

  private static async Task AuthorizeAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, AgentCaller caller, ActivityUpdate input)
  {
    // Match channel/member lock ordering used by sends and membership removal.
    await using (NpgsqlCommand channel = new("SELECT 1 FROM room_channels WHERE id=$1 AND project_id=$2 FOR SHARE", connection, transaction))
    {
      channel.Parameters.AddWithValue(input.ChannelId);
      channel.Parameters.AddWithValue(caller.Member.ProjectId);
      if (await channel.ExecuteScalarAsync() is null)
      {
        throw new KeyNotFoundException("That channel does not exist.");
      }
    }
    await using (NpgsqlCommand member = new("SELECT 1 FROM room_members m JOIN room_channel_members cm ON cm.member_id=m.id WHERE m.id=$1 AND m.session_hash=$2 AND NOT m.removed AND cm.channel_id=$3 FOR SHARE OF m,cm", connection, transaction))
    {
      member.Parameters.AddWithValue(caller.Member.Id);
      member.Parameters.AddWithValue(caller.SessionHash);
      member.Parameters.AddWithValue(input.ChannelId);
      if (await member.ExecuteScalarAsync() is null)
      {
        throw new UnauthorizedAccessException("This session cannot publish activity in that channel.");
      }
    }
    if (input.PacketId is not null)
    {
      await using NpgsqlCommand packet = new("SELECT 1 FROM room_packets p JOIN room_deliveries d ON d.packet_id=p.id WHERE p.id=$1 AND p.channel_id=$2 AND d.recipient_id=$3", connection, transaction);
      packet.Parameters.AddWithValue(input.PacketId.Value);
      packet.Parameters.AddWithValue(input.ChannelId);
      packet.Parameters.AddWithValue(caller.Member.Id);
      if (await packet.ExecuteScalarAsync() is null)
      {
        throw new ArgumentException("That source packet was not delivered to this agent in this channel.");
      }
    }
  }

  private static void Validate(ActivityUpdate input)
  {
    RequireText(input.Id, ActivityLimits.MaxId, false);
    RequireText(input.ChannelId, ActivityLimits.MaxId, false);
    RequireText(input.Phase, ActivityLimits.MaxId, false);
    RequireText(input.Label, ActivityLimits.MaxLabel, false);
    RequireText(input.Text, ActivityLimits.MaxText, true);
    if (input.Ordinal < 0 || input.Ordinal >= ActivityLimits.MaxSteps || (input.PacketId is not null && input.PacketId <= 0))
    {
      throw new ArgumentException("Activity position or source packet is invalid.");
    }
    if (input.State != "working" && input.State != "completed" && input.State != "failed")
    {
      throw new ArgumentException("Activity state is invalid.");
    }
  }

  private static void RequireText(string? text, int maximum, bool emptyAllowed)
  {
    if (text is null)
    {
      throw new ArgumentException("Activity fields cannot be null.");
    }
    if (text.Length > maximum || (!emptyAllowed && string.IsNullOrWhiteSpace(text)))
    {
      throw new ArgumentException("An activity field is empty or too long.");
    }
  }
}
