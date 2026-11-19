using Holodeck.Web.Rooms;
using Npgsql;

namespace Holodeck.Web.Management;

public sealed partial class ManagementStore
{
  public async Task<ManagementJob> CompleteAsync(AgentCaller caller, string id, ManagementCompletion input)
  {
    string payload = Payload(input.Result);
    await using NpgsqlConnection connection = await database.OpenConnectionAsync();
    await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
    await LockMemberAsync(connection, transaction, caller.Member, caller.SessionHash);
    ManagementJobState state = await ReadAsync(connection, transaction, caller.Member, id);
    if (state.Job.Status == ManagementStatus.Succeeded && SameAttempt(state, caller, input.AttemptToken))
    {
      await using NpgsqlCommand compare = new("SELECT result = $2 FROM management_jobs WHERE id = $1", connection, transaction);
      compare.Parameters.AddWithValue(id);
      AddJson(compare, payload);
      if (await compare.ExecuteScalarAsync() is not true)
      {
        throw new InvalidOperationException("This attempt already completed with a different result.");
      }

      return state.Job;
    }

    await RequireAttemptAsync(connection, transaction, caller, state, input.AttemptToken);
    Validate(state.Job.Action.ResultSchema, input.Result);
    // Validation can take time. The transition itself checks the lease again using database wall-clock time.
    await using NpgsqlCommand command = new("UPDATE management_jobs SET status = 'succeeded',result = $2,progress = $3,updated_at = clock_timestamp() WHERE id = $1 AND lease_expires_at > clock_timestamp()", connection, transaction);
    command.Parameters.AddWithValue(id);
    AddJson(command, payload);
    command.Parameters.AddWithValue(ManagementLimits.MaxProgress);
    await RequireTransitionAsync(command);
    ManagementJob result = (await ReadAsync(connection, transaction, caller.Member, id)).Job;
    await transaction.CommitAsync();
    return result;
  }

  public async Task<ManagementJob> FailAsync(AgentCaller caller, string id, ManagementFailure input)
  {
    RequireMessage(input.Error, ManagementLimits.MaxErrorLength, true);
    await using NpgsqlConnection connection = await database.OpenConnectionAsync();
    await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
    await LockMemberAsync(connection, transaction, caller.Member, caller.SessionHash);
    ManagementJobState state = await ReadAsync(connection, transaction, caller.Member, id);
    if (state.Job.Status == ManagementStatus.Failed && SameAttempt(state, caller, input.AttemptToken) && state.Job.Error == input.Error)
    {
      return state.Job;
    }

    await RequireAttemptAsync(connection, transaction, caller, state, input.AttemptToken);
    await using NpgsqlCommand command = new("UPDATE management_jobs SET status = 'failed',error = $2,updated_at = clock_timestamp() WHERE id = $1 AND lease_expires_at > clock_timestamp()", connection, transaction);
    command.Parameters.AddWithValue(id);
    command.Parameters.AddWithValue(input.Error!);
    await RequireTransitionAsync(command);
    ManagementJob result = (await ReadAsync(connection, transaction, caller.Member, id)).Job;
    await transaction.CommitAsync();
    return result;
  }

  public async Task<ManagementJob> CancelAsync(RoomMember member, string id)
  {
    await using NpgsqlConnection connection = await database.OpenConnectionAsync();
    await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
    ManagementJobState state = await ReadAsync(connection, transaction, member, id);
    if (state.Job.Status != ManagementStatus.Queued && state.Job.Status != ManagementStatus.Running)
    {
      return state.Job;
    }

    await using NpgsqlCommand command = new("UPDATE management_jobs SET status = 'canceled',updated_at = clock_timestamp() WHERE id = $1", connection, transaction);
    command.Parameters.AddWithValue(id);
    await command.ExecuteNonQueryAsync();
    ManagementJob result = (await ReadAsync(connection, transaction, member, id)).Job;
    await transaction.CommitAsync();
    return result;
  }

  private static bool SameAttempt(ManagementJobState state, AgentCaller caller, string? token)
  {
    return !string.IsNullOrEmpty(token) && state.AttemptToken == token && state.SessionHash == caller.SessionHash;
  }

  private static async Task RequireTransitionAsync(NpgsqlCommand command)
  {
    if (await command.ExecuteNonQueryAsync() != 1)
    {
      throw new InvalidOperationException("The job lease expired before this report could commit.");
    }
  }
}
