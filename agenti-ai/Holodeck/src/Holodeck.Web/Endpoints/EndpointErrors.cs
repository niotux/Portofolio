using Npgsql;

namespace Holodeck.Web.Endpoints;

// Maps the exceptions stores throw for bad requests onto HTTP statuses.
public static class EndpointErrors
{
  public static async Task<IResult> RunAsync(Func<Task<IResult>> operation)
  {
    try
    {
      return await operation();
    }
    catch (ArgumentException exception)
    {
      return Results.BadRequest(new ErrorResponse(exception.Message));
    }
    catch (KeyNotFoundException exception)
    {
      return Results.NotFound(new ErrorResponse(exception.Message));
    }
    catch (UnauthorizedAccessException exception)
    {
      return Results.Json(new ErrorResponse(exception.Message), statusCode: StatusCodes.Status401Unauthorized);
    }
    catch (InvalidOperationException exception)
    {
      return Results.Conflict(new ErrorResponse(exception.Message));
    }
    catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.ForeignKeyViolation)
    {
      return Results.Conflict(new ErrorResponse("The destination is missing, or the folder still contains items."));
    }
  }
}
