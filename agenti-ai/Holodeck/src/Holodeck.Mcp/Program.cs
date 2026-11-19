using Holodeck.Mcp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// An MCP server over stdio that drives Holodeck through its app API, as the browser does. The empty builder keeps the
// referenced app's appsettings.json out: the client's MCP configuration supplies Holodeck__Url.
HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { Args = args });
builder.Configuration.AddEnvironmentVariables();
builder.Configuration.AddCommandLine(args);
// Standard output carries the protocol, so every log line goes to standard error.
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Services.AddOptions<HolodeckApiOptions>().BindConfiguration("Holodeck").Validate(HolodeckApiOptions.IsValid, "Holodeck:Url must be an HTTP URL, for example Holodeck__Url=http://127.0.0.1:7077.").ValidateOnStart();
builder.Services.AddSingleton(CreateClient);
builder.Services.AddMcpServer().WithStdioServerTransport().WithTools<ProjectTools>().WithTools<AgentTools>().WithTools<ChannelTools>().WithTools<ComputerTools>().WithTools<ActionTools>();
await builder.Build().RunAsync();

static HolodeckClient CreateClient(IServiceProvider services)
{
  HolodeckApiOptions options = services.GetRequiredService<IOptions<HolodeckApiOptions>>().Value;
  return new HolodeckClient(new HttpClient { BaseAddress = new Uri(options.Url), Timeout = HolodeckClient.RequestTimeout });
}
