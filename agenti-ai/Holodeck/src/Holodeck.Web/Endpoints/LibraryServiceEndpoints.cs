using System.Net.Http.Headers;
using System.Net.WebSockets;
using Holodeck.Web.Computers;
using Holodeck.Web.Projects;
using Holodeck.Web.Rooms;
using Microsoft.AspNetCore.WebUtilities;

namespace Holodeck.Web.Endpoints;

// Only a declared applet can address its declared services. Never relay browser credentials to workloads.
public static class LibraryServiceEndpoints
{
  private const int BufferBytes = 32768;
  private static readonly TimeSpan ServiceTimeout = TimeSpan.FromMinutes(2);
  private static readonly HttpClient Http = new(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = ServiceTimeout };
  private static readonly string[] Methods = ["GET", "HEAD", "POST", "PUT", "PATCH", "DELETE"];

  public static void Map(WebApplication app)
  {
    app.MapMethods("/api/projects/{projectId}/libraries/{libraryId}/{version}/services/{service}/{**path}", Methods, ProxyAsync);
  }

  private static Task<IResult> ProxyAsync(string projectId, string libraryId, string version, string service, string? path, HttpContext context, ProjectApplications applications, CancellationToken token)
  {
    return EndpointErrors.RunAsync(async () =>
    {
      RequireOrigin(context);
      LibraryServiceConnection serviceConnection = await applications.ServiceAsync(projectId, libraryId, version, context.Request.Query["applet"].ToString(), service, token);
      string address = ServiceAddress(serviceConnection, path, context.Request.QueryString);
      if (context.WebSockets.IsWebSocketRequest)
      {
        await RelaySocketAsync(context, address, serviceConnection.Token, token);
      }
      else
      {
        await RelayHttpAsync(context, address, serviceConnection.Token, token);
      }

      return Results.Empty;
    });
  }

  private static void RequireOrigin(HttpContext context)
  {
    string origin = context.Request.Headers.Origin.ToString();
    if (origin.Length == 0)
    {
      return;
    }

    if (!Uri.TryCreate(origin, UriKind.Absolute, out Uri? source) || source.Authority != context.Request.Host.Value || source.Scheme != context.Request.Scheme)
    {
      throw new UnauthorizedAccessException("Service connections must originate from Holodeck.");
    }
  }

  private static string ServiceAddress(LibraryServiceConnection connection, string? path, QueryString query)
  {
    string relative = Uri.UnescapeDataString(path ?? "");
    if (relative.Contains('\\') || relative.Contains('?') || relative.Contains('#') || relative.StartsWith('/') || relative.Contains('%'))
    {
      throw new ArgumentException("Use a relative service path.");
    }

    foreach (string segment in relative.Split('/'))
    {
      if (segment == "." || segment == "..")
      {
        throw new ArgumentException("Service paths cannot traverse directories.");
      }
    }

    List<KeyValuePair<string, string?>> parameters = new();
    foreach (KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues> parameter in QueryHelpers.ParseQuery(query.Value))
    {
      if (parameter.Key == "applet")
      {
        continue;
      }

      foreach (string? value in parameter.Value)
      {
        parameters.Add(new KeyValuePair<string, string?>(parameter.Key, value));
      }
    }

    return $"http://{connection.Address}:{connection.Port}/{relative}{QueryString.Create(parameters)}";
  }

  private static async Task RelayHttpAsync(HttpContext context, string address, string secret, CancellationToken token)
  {
    using HttpRequestMessage request = new(new HttpMethod(context.Request.Method), address);
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
    if (context.Request.ContentLength > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding"))
    {
      request.Content = new StreamContent(context.Request.Body);
      if (context.Request.ContentType is not null)
      {
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(context.Request.ContentType);
      }
    }

    using HttpResponseMessage response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
    context.Response.StatusCode = (int)response.StatusCode;
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
    context.Response.ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
    await response.Content.CopyToAsync(context.Response.Body, token);
  }

  private static async Task RelaySocketAsync(HttpContext context, string address, string secret, CancellationToken token)
  {
    using ClientWebSocket service = new();
    service.Options.SetRequestHeader("Authorization", "Bearer " + secret);
    UriBuilder endpoint = new(address) { Scheme = "ws" };
    try
    {
      await service.ConnectAsync(endpoint.Uri, token);
    }
    catch (WebSocketException)
    {
      // The service refused or no longer has that resource; the applet decides what to do next.
      context.Response.StatusCode = StatusCodes.Status502BadGateway;
      return;
    }

    using WebSocket browser = await context.WebSockets.AcceptWebSocketAsync();
    using CancellationTokenSource connection = CancellationTokenSource.CreateLinkedTokenSource(token);
    Task upstream = PumpAsync(browser, service, connection.Token);
    Task downstream = PumpAsync(service, browser, connection.Token);
    await Task.WhenAny(upstream, downstream);
    await connection.CancelAsync();
    try
    {
      await Task.WhenAll(upstream, downstream);
    }
    catch (OperationCanceledException)
    {
    }
    catch (WebSocketException)
    {
      // Disconnecting a view never ends the application-owned backend process.
    }
  }

  private static async Task PumpAsync(WebSocket source, WebSocket destination, CancellationToken token)
  {
    byte[] buffer = new byte[BufferBytes];
    while (source.State == WebSocketState.Open && destination.State == WebSocketState.Open)
    {
      WebSocketReceiveResult received = await source.ReceiveAsync(buffer, token);
      if (received.MessageType == WebSocketMessageType.Close)
      {
        await destination.CloseOutputAsync(received.CloseStatus ?? WebSocketCloseStatus.NormalClosure, received.CloseStatusDescription, token);
        return;
      }

      await destination.SendAsync(buffer.AsMemory(0, received.Count), received.MessageType, received.EndOfMessage, token);
    }
  }
}
