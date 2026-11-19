using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Holodeck.Web.Computers;

// The few Docker Engine API calls library environments need, over plain HTTP to the socket proxy.
public sealed partial class DockerClient(IOptions<ComputerOptions> options)
{
  private const string ApiVersion = "/v1.43";
  private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
  private readonly HttpClient http = new() { BaseAddress = new Uri(options.Value.DockerUrl), Timeout = Timeout.InfiniteTimeSpan };

  // Pulling takes as long as it takes, so it runs without the request timeout.
  public async Task EnsureImageAsync(string image, CancellationToken token)
  {
    using HttpResponseMessage found = await SendAsync(HttpMethod.Get, $"/images/{Uri.EscapeDataString(image)}/json", null, token);
    if (found.StatusCode != HttpStatusCode.NotFound)
    {
      await RequireAsync(found, $"inspect image {image}");
      return;
    }

    using HttpRequestMessage request = new(HttpMethod.Post, $"{ApiVersion}/images/create?fromImage={Uri.EscapeDataString(image)}");
    using HttpResponseMessage pulling = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
    await RequireAsync(pulling, $"pull image {image}");
    await pulling.Content.CopyToAsync(Stream.Null, token);
  }

  public Task EnsureOpenNetworkAsync(string name, CancellationToken token)
  {
    return EnsureNetworkAsync(name, false, new Dictionary<string, string>(), token);
  }

  public Task EnsureInternalNetworkAsync(string name, Dictionary<string, string> labels, CancellationToken token)
  {
    return EnsureNetworkAsync(name, true, labels, token);
  }

  private async Task EnsureNetworkAsync(string name, bool internalNetwork, Dictionary<string, string> labels, CancellationToken token)
  {
    using HttpResponseMessage found = await SendAsync(HttpMethod.Get, $"/networks/{Uri.EscapeDataString(name)}", null, token);
    if (found.StatusCode != HttpStatusCode.NotFound)
    {
      await RequireAsync(found, $"inspect network {name}");
      return;
    }

    using HttpResponseMessage created = await SendAsync(HttpMethod.Post, "/networks/create", JsonContent.Create(new NetworkCreateRequest(name, internalNetwork, labels)), token);
    if (created.StatusCode == HttpStatusCode.Conflict)
    {
      // Another agent may have created the shared network after our inspection.
      using HttpResponseMessage existing = await SendAsync(HttpMethod.Get, $"/networks/{Uri.EscapeDataString(name)}", null, token);
      await RequireAsync(existing, $"inspect network {name}");
      return;
    }
    await RequireAsync(created, $"create network {name}");
  }

  public async Task ConnectNetworkAsync(string network, string containerId, CancellationToken token)
  {
    using HttpResponseMessage response = await SendAsync(HttpMethod.Post, $"/networks/{Uri.EscapeDataString(network)}/connect", JsonContent.Create(new NetworkConnectRequest(containerId)), token);
    await RequireAsync(response, $"join network {network}");
  }

  public async Task StartContainerAsync(string containerId, CancellationToken token)
  {
    using HttpResponseMessage response = await SendAsync(HttpMethod.Post, $"/containers/{containerId}/start", null, token);
    if (response.StatusCode != HttpStatusCode.NotModified)
    {
      await RequireAsync(response, "start the agent's computer");
    }
  }

  public async Task StopContainerAsync(string containerId, int graceSeconds, CancellationToken token)
  {
    using HttpResponseMessage response = await SendAsync(HttpMethod.Post, $"/containers/{containerId}/stop?t={graceSeconds}", null, token);
    if (response.StatusCode != HttpStatusCode.NotModified && response.StatusCode != HttpStatusCode.NotFound)
    {
      await RequireAsync(response, "stop the agent's computer");
    }
  }

  public async Task RemoveContainerAsync(string containerId, CancellationToken token)
  {
    using HttpResponseMessage response = await SendAsync(HttpMethod.Delete, $"/containers/{containerId}?force=1", null, token);
    if (response.StatusCode != HttpStatusCode.NotFound)
    {
      await RequireAsync(response, "remove the agent's computer");
    }
  }

  public async Task<JsonDocument?> TryInspectContainerAsync(string containerId, CancellationToken token)
  {
    using HttpResponseMessage response = await SendAsync(HttpMethod.Get, $"/containers/{Uri.EscapeDataString(containerId)}/json", null, token);
    if (response.StatusCode == HttpStatusCode.NotFound)
    {
      return null;
    }

    await RequireAsync(response, "inspect a container");
    return JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
  }

  public async Task<JsonDocument> InspectContainerAsync(string containerId, CancellationToken token)
  {
    using HttpResponseMessage response = await SendAsync(HttpMethod.Get, $"/containers/{Uri.EscapeDataString(containerId)}/json", null, token);
    await RequireAsync(response, "inspect a container");
    return JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
  }

  public async Task<List<string>> NetworkSubnetsAsync(string network, CancellationToken token)
  {
    using HttpResponseMessage response = await SendAsync(HttpMethod.Get, $"/networks/{Uri.EscapeDataString(network)}", null, token);
    await RequireAsync(response, $"inspect network {network}");
    List<string> subnets = new();
    using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
    JsonElement configs = body.RootElement.GetProperty("IPAM").GetProperty("Config");
    if (configs.ValueKind == JsonValueKind.Array)
    {
      foreach (JsonElement config in configs.EnumerateArray())
      {
        if (config.TryGetProperty("Subnet", out JsonElement subnet))
        {
          subnets.Add(subnet.GetString()!);
        }
      }
    }

    return subnets;
  }

  private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? content, CancellationToken token)
  {
    using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
    timeout.CancelAfter(RequestTimeout);
    using HttpRequestMessage request = new(method, ApiVersion + path) { Content = content };
    try
    {
      return await http.SendAsync(request, timeout.Token);
    }
    catch (HttpRequestException exception)
    {
      throw new InvalidOperationException($"Holodeck cannot reach Docker at {http.BaseAddress}: {exception.Message}", exception);
    }
  }

  private static async Task RequireAsync(HttpResponseMessage response, string action)
  {
    if (response.IsSuccessStatusCode)
    {
      return;
    }

    string detail = await response.Content.ReadAsStringAsync();
    throw new InvalidOperationException($"Docker could not {action}: {(int)response.StatusCode} {detail}");
  }
}
