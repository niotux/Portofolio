using Holodeck.Web.Rooms;
using Npgsql;

namespace Holodeck.Web.Management;

public sealed partial class ManagementStore
{
  public async Task<ManagementClaim?> ClaimAsync(AgentCaller caller, ManagementClaimInput input)
  {
    RequireIdentifier(input.ClaimId, "Claim Id");
    await using NpgsqlConnection connection = await database.OpenConnectionAsync();
    await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
    await LockMemberAsync(connection, transaction, caller.Member, caller.SessionHash);
    ManagementClaimState? prior = await FindClaimAsync(connection, transaction, caller, input.ClaimId!);
    if (prior is not null)
    {
      ManagementJobState job = await ReadAsync(connection, transaction, caller.Member, prior.JobId);
      await RequireAttemptAsync(connection, transaction, caller, job, prior.AttemptToken);
      return ClaimView(job);
    }

    string? id;
    await using (NpgsqlCommand select = new("SELECT id FROM management_jobs WHERE member_id = $1 AND (status = 'queued' OR (status = 'running' AND (lease_expires_at <= clock_timestamp() OR session_hash <> $2))) ORDER BY created_at,id LIMIT 1 FOR UPDATE SKIP LOCKED", connection, transaction))
    {
      select.Parameters.AddWithValue(caller.Member.Id);
      select.Parameters.AddWithValue(caller.SessionHash);
      id = (string?)await select.ExecuteScalarAsync();
    }

    string? attempt = null;
    ManagementClaim? result = null;
    if (id is not null)
    {
      attempt = NewToken();
      await using NpgsqlCommand update = new("UPDATE management_jobs SET status = 'running',session_hash = $2,attempt_token = $3,lease_expires_at = clock_timestamp() + make_interval(secs => $4),progress = NULL,message = NULL,error = NULL,updated_at = clock_timestamp() WHERE id = $1", connection, transaction);
      update.Parameters.AddWithValue(id);
      update.Parameters.AddWithValue(caller.SessionHash);
      update.Parameters.AddWithValue(attempt);
      update.Parameters.AddWithValue(ManagementLimits.LeaseSeconds);
      await update.ExecuteNonQueryAsync();
      result = ClaimView(await ReadAsync(connection, transaction, caller.Member, id));
    }

    // An empty poll makes no side effect and needs no durable record. Real claims retain their request identity.
    if (id is not null)
    {
      await using NpgsqlCommand insert = new("INSERT INTO management_claims(member_id,session_hash,claim_id,job_id,attempt_token) VALUES ($1,$2,$3,$4,$5)", connection, transaction);
      insert.Parameters.AddWithValue(caller.Member.Id);
      insert.Parameters.AddWithValue(caller.SessionHash);
      insert.Parameters.AddWithValue(input.ClaimId!);
      insert.Parameters.AddWithValue(id);
      insert.Parameters.AddWithValue(attempt!);
      await insert.ExecuteNonQueryAsync();
    }

    await transaction.CommitAsync();
    return result;
  }

  public async Task<ManagementJob> ProgressAsync(AgentCaller caller, string id, ManagementProgress input)
  {
    if (input.Progress is not null && (input.Progress < 0 || input.Progress > ManagementLimits.MaxProgress))
    {
      throw new ArgumentException($"Progress must be 0 to {ManagementLimits.MaxProgress}.");
    }

    RequireMessage(input.Message, ManagementLimits.MaxMessageLength, false);
    await using NpgsqlConnection connection = await database.OpenConnectionAsync();
    await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
    await LockMemberAsync(connection, transaction, caller.Member, caller.SessionHash);
    ManagementJobState state = await ReadAsync(connection, transaction, caller.Member, id);
    await RequireAttemptAsync(connection, transaction, caller, state, input.AttemptToken);
    await using NpgsqlCommand update = new("UPDATE management_jobs SET progress = $2,message = $3,lease_expires_at = clock_timestamp() + make_interval(secs => $4),updated_at = clock_timestamp() WHERE id = $1 AND lease_expires_at > clock_timestamp()", connection, transaction);
    update.Parameters.AddWithValue(id);
    update.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Integer, (object?)input.Progress ?? DBNull.Value);
    update.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Text, (object?)input.Message ?? DBNull.Value);
    update.Parameters.AddWithValue(ManagementLimits.LeaseSeconds);
    await RequireTransitionAsync(update);
    ManagementJob result = (await ReadAsync(connection, transaction, caller.Member, id)).Job;
    await transaction.CommitAsync();
    return result;
  }

  private static async Task<ManagementClaimState?> FindClaimAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, AgentCaller caller, string claimId)
  {
    await using NpgsqlCommand command = new("SELECT job_id,attempt_token FROM management_claims WHERE member_id = $1 AND session_hash = $2 AND claim_id = $3", connection, transaction);
    command.Parameters.AddWithValue(caller.Member.Id);
    command.Parameters.AddWithValue(caller.SessionHash);
    command.Parameters.AddWithValue(claimId);
    await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
      return null;
    }

    return new ManagementClaimState(reader.GetString(0), reader.GetString(1));
  }

  private static ManagementClaim ClaimView(ManagementJobState state)
  {
    return new ManagementClaim(state.Job, state.SettingsContent, state.AttemptToken!, state.LeaseExpiresAt!.Value);
  }

  private static async Task RequireAttemptAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, AgentCaller caller, ManagementJobState state, string? attemptToken)
  {
    if (string.IsNullOrEmpty(attemptToken) || state.Job.Status != ManagementStatus.Running || state.SessionHash != caller.SessionHash || state.AttemptToken != attemptToken)
    {
      throw new InvalidOperationException("That job attempt is no longer active.");
    }

    await using NpgsqlCommand command = new("SELECT lease_expires_at > clock_timestamp() FROM management_jobs WHERE id = $1", connection, transaction);
    command.Parameters.AddWithValue(state.Job.Id);
    if (await command.ExecuteScalarAsync() is not true)
    {
      throw new InvalidOperationException("That job attempt lease expired. Claim it again before reporting.");
    }
  }

  private static void RequireMessage(string? message, int maximum, bool required)
  {
    if (message is null)
    {
      if (required)
      {
        throw new ArgumentException("Provide an error message.");
      }

      return;
    }

    if (message.Length > maximum || (required && string.IsNullOrWhiteSpace(message)))
    {
      throw new ArgumentException($"Keep the message between 1 and {maximum} characters.");
    }
  }
}
