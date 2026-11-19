using Npgsql;

namespace Holodeck.Web.Rooms;

// One-time move from two-party wires to channels. Each wire becomes a legacy channel with the same id, so saved
// references, history, queue positions and delivered file paths all carry over unchanged.
public sealed partial class RoomStore
{
  private const string ChannelsMigration = "channels";

  private async Task MigrateWiresAsync(CancellationToken token)
  {
    const string Names = """
      SELECT w.id, w.project_id,
        CASE WHEN a.is_you THEN b.name WHEN b.is_you THEN a.name
             WHEN a.created_at <= b.created_at THEN a.name || ' & ' || b.name ELSE b.name || ' & ' || a.name END
      FROM room_wires w JOIN room_members a ON a.id = w.first_id JOIN room_members b ON b.id = w.second_id
      ORDER BY w.project_id, w.created_at, w.id
      """;
    const string Steps = """
      INSERT INTO room_channel_members(channel_id, member_id)
      SELECT w.id, m.id FROM room_wires w JOIN room_members m ON m.id = w.first_id OR m.id = w.second_id
      WHERE w.connected AND NOT m.removed;
      UPDATE room_packets SET channel_id = wire_id WHERE channel_id IS NULL AND wire_id IS NOT NULL;
      INSERT INTO room_deliveries(packet_id, recipient_id, recipient_sequence)
      SELECT id, recipient_id, recipient_sequence FROM room_packets WHERE recipient_id IS NOT NULL AND recipient_sequence IS NOT NULL;
      INSERT INTO room_delivery_files(packet_id, recipient_id, ordinal, delivered_path)
      SELECT f.packet_id, p.recipient_id, f.ordinal, f.delivered_path
      FROM room_packet_files f JOIN room_packets p ON p.id = f.packet_id
      WHERE f.delivered_path IS NOT NULL AND p.recipient_id IS NOT NULL;
      """;
    await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
    await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token);
    await using (NpgsqlCommand mark = new("INSERT INTO room_migrations(name) VALUES ($1) ON CONFLICT DO NOTHING", connection, transaction))
    {
      mark.Parameters.AddWithValue(ChannelsMigration);
      if (await mark.ExecuteNonQueryAsync(token) == 0)
      {
        return;
      }
    }

    List<LegacyWireName> wires = new();
    await using (NpgsqlCommand names = new(Names, connection, transaction))
    {
      await using NpgsqlDataReader reader = await names.ExecuteReaderAsync(token);
      while (await reader.ReadAsync(token))
      {
        wires.Add(new LegacyWireName(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
      }
    }

    // Names are given in C# with the same numbering as placement, so "Echo" twice next to "Echo 2" cannot collide.
    List<string> taken = new();
    string projectId = "";
    foreach (LegacyWireName wire in wires)
    {
      if (wire.ProjectId != projectId)
      {
        projectId = wire.ProjectId;
        taken.Clear();
      }

      string name = FreeName(wire.BaseName, taken);
      taken.Add(name);
      await using NpgsqlCommand insert = new("INSERT INTO room_channels(id, project_id, name, legacy, paused, created_at) SELECT id, project_id, $2, true, false, created_at FROM room_wires WHERE id = $1", connection, transaction);
      insert.Parameters.AddWithValue(wire.Id);
      insert.Parameters.AddWithValue(name);
      await insert.ExecuteNonQueryAsync(token);
    }

    await using (NpgsqlCommand migrate = new(Steps, connection, transaction))
    {
      await migrate.ExecuteNonQueryAsync(token);
    }

    await transaction.CommitAsync(token);
  }
}
