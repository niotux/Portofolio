using Humans;
using Humans.PromptTools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// An MCP server over stdio for editing Humans prompts live and publishing them. Any MCP client can run it.
// appsettings.json beside the program configures Holodeck; environment variables such as Holodeck__Url override it.
HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { Args = args });
builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false);
builder.Configuration.AddEnvironmentVariables();
builder.Configuration.AddCommandLine(args);
// Standard output carries the protocol, so every log line goes to standard error.
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Services.AddOptions<HolodeckOptions>().BindConfiguration("Holodeck").Validate(HolodeckOptions.IsValid, "Configure Holodeck:Url, Holodeck:Library and Holodeck:LibraryVersion.").ValidateOnStart();
builder.Services.AddSingleton(CreateStore);
builder.Services.AddMcpServer().WithStdioServerTransport().WithTools<PromptTools>();
await builder.Build().RunAsync();

static PromptStore CreateStore(IServiceProvider services)
{
  HolodeckOptions options = services.GetRequiredService<IOptions<HolodeckOptions>>().Value;
  Uri endpoint = new(new Uri(options.Url), "api/libraries/" + Uri.EscapeDataString(options.Library) + "/" + Uri.EscapeDataString(options.LibraryVersion) + "/database");
  HttpClient http = new() { Timeout = TimeSpan.FromSeconds(PromptTools.RequestSeconds) };
  return new PromptStore(new LibraryDatabase(http, endpoint, null));
}
