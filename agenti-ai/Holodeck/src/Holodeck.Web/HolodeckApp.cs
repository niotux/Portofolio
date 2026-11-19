using System.Net;
using Holodeck.Web.Activity;
using Holodeck.Web.AgentData;
using Holodeck.Web.Computers;
using Holodeck.Web.Configuration;
using Holodeck.Web.Appearance;
using Holodeck.Web.Endpoints;
using Holodeck.Web.Projects;
using Holodeck.Web.Rooms;
using Holodeck.Web.Settings;
using Holodeck.Web.Management;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Holodeck.Web;

// Composition shared by Program and the tests' host.
public static class HolodeckApp
{
  public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
  {
    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
    if (configure is not null)
    {
      configure(builder);
    }

    builder.Services.AddOptions<StudioOptions>().BindConfiguration("Studio").Validate(ValidateDataPath, "Studio:DataPath is required.").ValidateOnStart();
    builder.Services.AddOptions<DatabaseOptions>().BindConfiguration("Database").Validate(ValidateConnectionString, "Database connection string is required.").ValidateOnStart();
    builder.Services.AddOptions<ComputerOptions>().BindConfiguration("Computer").PostConfigure(DropBlankFolderRoots);
    builder.Services.AddOptions<SupervisorOptions>().BindConfiguration("Supervisor").Validate(ValidateHolodeckUrl, "Supervisor:HolodeckUrl must be an HTTP URL.").ValidateOnStart();
    builder.WebHost.ConfigureKestrel(ConfigureKestrel);
    builder.Services.Configure<FormOptions>(ConfigureForms);
    builder.Services.AddSingleton(CreateDatabase);
    builder.Services.AddSingleton<ProjectStore>();
    builder.Services.AddSingleton<ProjectLibraryStore>();
    builder.Services.AddSingleton<ProjectWorkspaceStore>();
    builder.Services.AddSingleton<AppearanceStore>();
    builder.Services.AddSingleton<GrantStore>();
    builder.Services.AddSingleton<ComputerHost>();
    builder.Services.AddSingleton<DockerClient>();
    builder.Services.AddSingleton<LibraryEnvironments>();
    builder.Services.AddSingleton<ProjectApplications>();
    builder.Services.AddSingleton<ProjectExecutionStore>();
    builder.Services.AddSingleton<LibraryAgentRuntime>();
    builder.Services.TryAddSingleton<IAgentRuntime>(services => services.GetRequiredService<LibraryAgentRuntime>());
    builder.Services.AddSingleton<RoomPaths>();
    builder.Services.AddSingleton<AgentLibrary>();
    builder.Services.AddSingleton<RoomStore>();
    builder.Services.AddSingleton<RoomSignals>();
    builder.Services.AddSingleton<PacketStore>();
    builder.Services.AddSingleton<ActivityStore>();
    builder.Services.AddSingleton<AgentDatabases>();
    builder.Services.AddSingleton<AgentSettingsStore>();
    builder.Services.AddSingleton<ManagementStore>();
    builder.Services.AddSingleton<AgentSupervisor>();
    builder.Services.AddHostedService(services => services.GetRequiredService<AgentSupervisor>());
    builder.Services.AddHostedService(services => services.GetRequiredService<ProjectApplications>());
    return builder.Build();
  }

  public static async Task InitializeAsync(WebApplication app)
  {
    await app.Services.GetRequiredService<ProjectStore>().InitializeAsync(CancellationToken.None);
    await app.Services.GetRequiredService<RoomStore>().InitializeAsync(CancellationToken.None);
    await app.Services.GetRequiredService<ProjectLibraryStore>().InitializeAsync(CancellationToken.None);
    await app.Services.GetRequiredService<ProjectWorkspaceStore>().InitializeAsync(CancellationToken.None);
    await app.Services.GetRequiredService<ProjectExecutionStore>().InitializeAsync();
    await app.Services.GetRequiredService<AppearanceStore>().InitializeAsync(CancellationToken.None);
    await app.Services.GetRequiredService<GrantStore>().InitializeAsync(CancellationToken.None);
    await app.Services.GetRequiredService<ManagementStore>().InitializeAsync();
    await app.Services.GetRequiredService<ActivityStore>().InitializeAsync();
    await app.Services.GetRequiredService<AgentDatabases>().InitializeAsync();
    app.Use(GuardAgentPortAsync);
    app.Use(GuardUserOriginAsync);
    app.UseWebSockets();
    app.UseDefaultFiles();
    // Revalidate the UI on every load (cheap with ETags), so a rebuilt app is never hidden behind a cached old script.
    // The page also clears this site's cache: module scripts cached before no-cache existed would otherwise stay
    // heuristically fresh, and browsers do not reliably bypass the cache for imported modules on a hard reload.
    app.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = PrepareStaticFile });
    app.MapGet("/health", () => Results.Ok(new HealthResponse("ok")));
    ProjectEndpoints.Map(app);
    ProjectLibraryEndpoints.Map(app);
    ProjectServiceEndpoints.Map(app);
    ProjectWorkspaceEndpoints.Map(app);
    LibraryAppletEndpoints.Map(app);
    LibraryServiceEndpoints.Map(app);
    AppearanceEndpoints.Map(app);
    ComputerEndpoints.Map(app);
    RoomEndpoints.Map(app);
    AgentEndpoints.Map(app);
    LibraryDatabaseEndpoints.Map(app);
    ActivityEndpoints.Map(app);
    ProjectEventEndpoints.Map(app);
    ManagementEndpoints.Map(app);
  }

  private static async Task GuardUserOriginAsync(HttpContext context, RequestDelegate next)
  {
    if (!UserApiOriginGuard.Allows(context.Request))
    {
      context.Response.StatusCode = StatusCodes.Status401Unauthorized;
      await context.Response.WriteAsJsonAsync(new ErrorResponse("User API changes must originate from Holodeck."));
      return;
    }

    await next(context);
  }

  private static async Task GuardAgentPortAsync(HttpContext context, RequestDelegate next)
  {
    ComputerOptions computer = context.RequestServices.GetRequiredService<IOptions<ComputerOptions>>().Value;
    List<IPNetwork> subnets = context.RequestServices.GetRequiredService<ComputerHost>().AgentSubnets;
    int status = AgentPortGuard.Decide(context.Connection.LocalPort, context.Connection.RemoteIpAddress, context.Request.Path.Value ?? "", subnets, computer.UserPort, computer.AgentPort);
    if (status != AgentPortGuard.Allowed)
    {
      context.Response.StatusCode = status;
      return;
    }

    await next(context);
  }

  private static void PrepareStaticFile(StaticFileResponseContext file)
  {
    file.Context.Response.Headers.CacheControl = "no-cache";
    bool isPage = string.Equals(Path.GetExtension(file.File.Name), ".html", StringComparison.OrdinalIgnoreCase);
    if (isPage)
    {
      file.Context.Response.Headers["Clear-Site-Data"] = "\"cache\"";
    }
  }

  private static void ConfigureKestrel(KestrelServerOptions options)
  {
    options.Limits.MaxRequestBodySize = RoomLimits.MaxRequestBytes;
  }

  private static void ConfigureForms(FormOptions options)
  {
    options.MultipartBodyLengthLimit = RoomLimits.MaxRequestBytes;
  }

  // Compose passes the folder root from .env, which is blank when this machine shares no folders.
  private static void DropBlankFolderRoots(ComputerOptions options)
  {
    for (int index = options.AllowedFolderRoots.Count - 1; index >= 0; index--)
    {
      if (string.IsNullOrWhiteSpace(options.AllowedFolderRoots[index]))
      {
        options.AllowedFolderRoots.RemoveAt(index);
      }
    }
  }

  private static bool ValidateDataPath(StudioOptions options)
  {
    return !string.IsNullOrWhiteSpace(options.DataPath);
  }

  private static bool ValidateConnectionString(DatabaseOptions options)
  {
    return !string.IsNullOrWhiteSpace(options.ConnectionString);
  }

  private static bool ValidateHolodeckUrl(SupervisorOptions options)
  {
    if (!Uri.TryCreate(options.HolodeckUrl, UriKind.Absolute, out Uri? uri))
    {
      return false;
    }

    return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
  }

  private static NpgsqlDataSource CreateDatabase(IServiceProvider services)
  {
    DatabaseOptions options = services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
    return NpgsqlDataSource.Create(options.ConnectionString);
  }
}
