using System.Collections.Concurrent;
using System.Text.Json;
using Holodeck.Web.Computers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Holodeck.Tests;

// A small Docker HTTP boundary for checking container identity without a real daemon or database.
internal sealed class FakeDockerEngine : IAsyncDisposable
{
  private readonly WebApplication app;
  private readonly Dictionary<string, FakeDockerComputer> computers = new();
  private int nextId;

  private FakeDockerEngine(WebApplication app, string dataPath)
  {
    this.app = app;
    DataPath = dataPath;
  }

  public string Address { get; private set; } = "";
  public string DataPath { get; }
  public ConcurrentQueue<string> Requests { get; } = new();
  public Dictionary<string, NetworkCreateRequest> Networks { get; } = new()
  {
    ["agents"] = new NetworkCreateRequest("agents", true, new Dictionary<string, string>())
  };
  public int NetworkCreationStatus { get; set; } = StatusCodes.Status201Created;

  public FakeDockerComputer Computer(string name) => computers[name];

  public static async Task<FakeDockerEngine> StartAsync(string dataPath)
  {
    WebApplicationBuilder builder = WebApplication.CreateBuilder();
    builder.Logging.ClearProviders();
    builder.WebHost.UseUrls("http://127.0.0.1:0");
    WebApplication app = builder.Build();
    FakeDockerEngine engine = new(app, dataPath);
    app.Run(engine.HandleAsync);
    await app.StartAsync();
    foreach (string address in app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses)
    {
      engine.Address = address;
      break;
    }
    return engine;
  }

  public void Exit(string name, int code)
  {
    FakeDockerComputer computer = computers[name];
    computer.Running = false;
    computer.Exit.TrySetResult(code);
  }

  private async Task HandleAsync(HttpContext context)
  {
    string path = context.Request.Path.Value!["/v1.43".Length..];
    string method = context.Request.Method;
    Requests.Enqueue(method + " " + path);
    if (path.StartsWith("/images/", StringComparison.Ordinal))
    {
      await JsonAsync(context, "{\"Config\":{\"Labels\":{\"holodeck.image\":\"test\"}}}");
      return;
    }
    if (path == "/containers/" + Environment.MachineName + "/json")
    {
      string data = JsonSerializer.Serialize(DataPath);
      string libraries = JsonSerializer.Serialize(Path.Combine(DataPath, "libs"));
      await JsonAsync(context, "{\"Mounts\":[{\"Destination\":" + data + ",\"Source\":" + data + "},{\"Destination\":" + libraries + ",\"Source\":" + libraries + "}],\"Config\":{\"Labels\":{}}}");
      return;
    }
    if (path == "/containers/json")
    {
      await JsonAsync(context, "[]");
      return;
    }
    if (path == "/networks")
    {
      await JsonAsync(context, "[]");
      return;
    }
    if (path == "/networks/create" && method == "POST")
    {
      context.Response.StatusCode = NetworkCreationStatus;
      if (NetworkCreationStatus != StatusCodes.Status201Created)
      {
        return;
      }
      NetworkCreateRequest network = (await context.Request.ReadFromJsonAsync<NetworkCreateRequest>())!;
      Networks.Add(network.Name, network);
      await JsonAsync(context, "{\"Id\":\"" + network.Name + "\"}");
      return;
    }
    if (path.StartsWith("/networks/", StringComparison.Ordinal))
    {
      string name = path["/networks/".Length..];
      if (path.EndsWith("/connect", StringComparison.Ordinal))
      {
        name = name[..^"/connect".Length];
      }
      if (!Networks.ContainsKey(name))
      {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
      }
      if (path.EndsWith("/connect", StringComparison.Ordinal))
      {
        using JsonDocument body = await JsonDocument.ParseAsync(context.Request.Body);
        string id = body.RootElement.GetProperty("container").GetString()!;
        Find(id)!.Networks[name] = new object();
      }
      await JsonAsync(context, "{\"IPAM\":{\"Config\":[]}}");
      return;
    }
    if (path == "/containers/create")
    {
      string name = context.Request.Query["name"].ToString();
      using JsonDocument body = await JsonDocument.ParseAsync(context.Request.Body);
      string network = body.RootElement.GetProperty("hostConfig").GetProperty("networkMode").GetString()!;
      if (!Networks.ContainsKey(network))
      {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
      }
      FakeDockerComputer computer = new("computer-" + ++nextId, name, body.RootElement.Clone());
      computer.Networks[network] = new object();
      computers.Add(name, computer);
      await JsonAsync(context, "{\"Id\":\"" + computer.Id + "\"}");
      return;
    }
    string[] parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
    FakeDockerComputer? found = parts.Length >= 2 ? Find(parts[1]) : null;
    if (found is null)
    {
      context.Response.StatusCode = StatusCodes.Status404NotFound;
      return;
    }
    if (method == "DELETE")
    {
      Exit(found.Name, 137);
      computers.Remove(found.Name);
      return;
    }
    string action = parts[2];
    if (action == "json")
    {
      string running = found.Running ? "true" : "false";
      string env = found.Config.TryGetProperty("env", out JsonElement entries) ? entries.GetRawText() : "[]";
      string labels = found.Config.GetProperty("labels").GetRawText();
      string config = "{\"Env\":" + env + ",\"Labels\":" + labels + "}";
      string networks = JsonSerializer.Serialize(found.Networks);
      await JsonAsync(context, "{\"Id\":\"" + found.Id + "\",\"Config\":" + config + ",\"State\":{\"Running\":" + running + ",\"Status\":\"" + (found.Running ? "running" : "exited") + "\",\"ExitCode\":0},\"NetworkSettings\":{\"Networks\":" + networks + "}}");
    }
    else if (action == "start")
    {
      string network = found.Config.GetProperty("hostConfig").GetProperty("networkMode").GetString()!;
      if (!Networks.ContainsKey(network))
      {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
      }
      if (found.Running)
      {
        context.Response.StatusCode = StatusCodes.Status304NotModified;
        return;
      }
      found.Exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
      found.Running = true;
    }
    else if (action == "stop")
    {
      Exit(found.Name, 0);
    }
    else if (action == "wait")
    {
      int code = await found.Exit.Task.WaitAsync(context.RequestAborted);
      await JsonAsync(context, "{\"StatusCode\":" + code + "}");
    }
  }

  private FakeDockerComputer? Find(string id)
  {
    foreach (FakeDockerComputer computer in computers.Values)
    {
      if (computer.Name == id || computer.Id == id)
      {
        return computer;
      }
    }
    return null;
  }

  private static Task JsonAsync(HttpContext context, string json)
  {
    context.Response.ContentType = "application/json";
    return context.Response.WriteAsync(json);
  }

  public async ValueTask DisposeAsync()
  {
    await app.StopAsync();
    await app.DisposeAsync();
  }
}
