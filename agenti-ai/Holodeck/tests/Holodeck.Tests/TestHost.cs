using Holodeck.Web;
using Holodeck.Web.Computers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Holodeck.Tests;

// The real application pipeline on a free local port, over a test schema and data directory.
internal sealed class TestHost : IAsyncDisposable
{
  private readonly WebApplication app;

  private TestHost(WebApplication app, HttpClient client)
  {
    this.app = app;
    Client = client;
  }

  public HttpClient Client { get; }

  public T Get<T>() where T : notnull
  {
    return app.Services.GetRequiredService<T>();
  }

  public static async Task<TestHost> StartAsync(TestDatabase database, List<string>? folderRoots = null)
  {
    Dictionary<string, string?> settings = new()
    {
      ["Studio:DataPath"] = database.DataPath,
      ["Studio:LibrariesPath"] = Path.Combine(database.DataPath, "libs"),
      ["Database:ConnectionString"] = database.ScopedConnectionString,
      ["Supervisor:HolodeckUrl"] = "http://holodeck.test",
      ["Computer:PortRangeStart"] = "20000",
      ["Computer:PortRangeEnd"] = "20999"
    };
    if (folderRoots is not null)
    {
      for (int index = 0; index < folderRoots.Count; index++)
      {
        settings[$"Computer:AllowedFolderRoots:{index}"] = folderRoots[index];
      }
    }
    WebApplication app = HolodeckApp.Build([], builder =>
    {
      builder.Configuration.AddInMemoryCollection(settings);
      builder.Services.AddSingleton<IAgentRuntime, ProcessAgentRuntime>();
      builder.WebHost.UseUrls("http://127.0.0.1:0");
    });
    await HolodeckApp.InitializeAsync(app);
    await app.StartAsync();
    string address = "";
    foreach (string bound in app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses)
    {
      address = bound;
      break;
    }

    HttpClient client = new() { BaseAddress = new Uri(address) };
    return new TestHost(app, client);
  }

  public async ValueTask DisposeAsync()
  {
    Client.Dispose();
    await app.StopAsync();
    await app.DisposeAsync();
  }
}
