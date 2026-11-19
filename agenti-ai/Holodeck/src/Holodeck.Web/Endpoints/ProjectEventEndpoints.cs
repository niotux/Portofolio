using System.Net.WebSockets;
using System.Text.Json;
using Holodeck.Web.Rooms;

namespace Holodeck.Web.Endpoints;

// One WebSocket per page and project carries every change notice. Browsers allow only a few HTTP/1.1 connections to a
// server and long-lived streams would hold them, so ordinary requests could wait forever; WebSockets are not counted.
public static class ProjectEventEndpoints
{
  public const string RoomKind = "room";
  public const string ActivityKind = "activity";
  private const int ReceiveBufferBytes = 256;
  private static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(20);

  public static void Map(WebApplication app)
  {
    app.Map("/api/projects/{projectId}/events", StreamAsync);
  }

  private static async Task StreamAsync(string projectId, HttpContext context, RoomSignals signals)
  {
    if (!context.WebSockets.IsWebSocketRequest)
    {
      context.Response.StatusCode = StatusCodes.Status400BadRequest;
      return;
    }

    using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync(new WebSocketAcceptContext { KeepAliveInterval = KeepAlive });
    using CancellationTokenSource connection = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
    CancellationToken token = connection.Token;
    Task closed = WaitForCloseAsync(socket, token);
    // Each wait is registered before the notice that ends the previous one goes out, so a change made after a notice
    // always wakes the next wait and a client that reloads on a notice never misses one.
    Task room = signals.WaitForProjectAsync(projectId, token);
    Task activity = signals.WaitForActivityAsync(projectId, token);
    try
    {
      while (socket.State == WebSocketState.Open)
      {
        Task finished = await Task.WhenAny(room, activity, closed);
        if (finished == closed || token.IsCancellationRequested)
        {
          break;
        }

        if (room.IsCompleted)
        {
          room = signals.WaitForProjectAsync(projectId, token);
          await SendAsync(socket, RoomKind, token);
        }

        if (activity.IsCompleted)
        {
          activity = signals.WaitForActivityAsync(projectId, token);
          await SendAsync(socket, ActivityKind, token);
        }
      }
    }
    catch (OperationCanceledException)
    {
    }
    catch (WebSocketException)
    {
      // The page went away; it reconnects when it comes back.
    }
    finally
    {
      await connection.CancelAsync();
    }
  }

  private static Task SendAsync(WebSocket socket, string kind, CancellationToken token)
  {
    byte[] message = JsonSerializer.SerializeToUtf8Bytes(new ProjectChange(kind), JsonSerializerOptions.Web);
    return socket.SendAsync(message, WebSocketMessageType.Text, true, token);
  }

  // Pages send nothing; reading only notices when they close.
  private static async Task WaitForCloseAsync(WebSocket socket, CancellationToken token)
  {
    byte[] buffer = new byte[ReceiveBufferBytes];
    try
    {
      while (socket.State == WebSocketState.Open)
      {
        WebSocketReceiveResult received = await socket.ReceiveAsync(buffer, token);
        if (received.MessageType == WebSocketMessageType.Close)
        {
          await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, token);
          return;
        }
      }
    }
    catch (OperationCanceledException)
    {
    }
    catch (WebSocketException)
    {
    }
  }
}
