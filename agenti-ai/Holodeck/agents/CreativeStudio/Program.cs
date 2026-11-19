using System.Runtime.InteropServices;
using System.Text.Json;

namespace CreativeStudio;

public static class Program
{
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

  public static async Task<int> Main()
  {
    using CancellationTokenSource stop = new();
    using PosixSignalRegistration terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context => Cancel(context, stop));
    using PosixSignalRegistration interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, context => Cancel(context, stop));
    try
    {
      string library = Required("HOLODECK_LIBRARY");
      string workspace = Required("HOLODECK_TABLE");
      AgentConfiguration settings = Read<AgentConfiguration>(Required("HOLODECK_SETTINGS"));
      RuntimeOptions options = Read<RuntimeOptions>(Path.Combine(library, "runtime.json"));
      Validate(settings, options);
      using HttpClient transportHttp = new() { Timeout = TimeSpan.FromSeconds(options.DatabaseTimeoutSeconds) };
      using HttpClient activityHttp = new() { Timeout = TimeSpan.FromSeconds(options.DatabaseTimeoutSeconds) };
      using HttpClient modelHttp = new() { Timeout = Timeout.InfiniteTimeSpan };
      using HttpClient embeddingHttp = new() { Timeout = Timeout.InfiniteTimeSpan };
      using HttpClient comfyHttp = new() { Timeout = Timeout.InfiniteTimeSpan };
      Uri address = new(Required("HOLODECK_URL").TrimEnd('/') + "/");
      string key = Required("HOLODECK_KEY");
      HolodeckClient transport = new(transportHttp, address, key);
      HolodeckClient activity = new(activityHttp, address, key);
      CreativeStore store = new(transport);
      PromptRegistry prompts = new(library, store, options.MaxPromptCharacters);
      await InitializeAsync(transport, store, prompts, settings.Embedding.Dimensions, options.PollSeconds, stop.Token);
      WorkflowCatalog workflows = new(library);
      CreativeModel model = new(modelHttp, settings.Model);
      EmbeddingClient embedding = new(embeddingHttp, settings.Embedding);
      ComfyClient comfy = new(comfyHttp, settings.Comfy, workspace);
      WorkspaceFiles files = new(workspace);
      await WorkspaceIndex.RestoreAsync(store, files, workspace, options.SearchResults, stop.Token);
      ReferencePreview previews = new(workspace, options.MaxReferenceEdge, options.PreviewTimeoutSeconds);
      StudioTools instruments = new(store, embedding, workflows, files, previews, options);
      StudioToolCatalog controls = new(library);
      StudioConversation conversation = new(model, store, prompts, instruments, controls, workflows, options, activity);
      GenerationWorker worker = new(store, comfy, activity, options, settings.Comfy);
      PromptActions actions = new(transport, prompts, store, comfy, options.SearchResults);
      StudioAgent agent = new(transport, store, prompts, conversation, worker, actions, options);
      Console.WriteLine("Creative Studio is ready. Conversation, notebook and image jobs use retained private storage.");
      await agent.RunAsync(stop.Token);
      return 0;
    }
    catch (OperationCanceledException) when (stop.IsCancellationRequested)
    {
      return 0;
    }
    catch (UnauthorizedAccessException)
    {
      return 0;
    }
    catch (Exception exception)
    {
      Console.WriteLine("Creative Studio stopped: " + exception.Message);
      return 1;
    }
  }

  private static async Task InitializeAsync(HolodeckClient transport, CreativeStore store, PromptRegistry prompts, int dimensions, int retrySeconds, CancellationToken token)
  {
    while (true)
    {
      try
      {
        AgentIdentity identity = await transport.MeAsync(token);
        if (identity.Capabilities is null)
        {
          throw new IOException("This agent requires Holodeck database and idempotent-send capabilities.");
        }

        if (!identity.Capabilities.Contains("database-v1") || !identity.Capabilities.Contains("idempotent-send-v1"))
        {
          throw new IOException("This agent requires Holodeck database and idempotent-send capabilities.");
        }

        await store.InitializeAsync(dimensions, token);
        await prompts.InitializeAsync(token);
        return;
      }
      catch (Exception exception) when (exception is HttpRequestException || (exception is OperationCanceledException && !token.IsCancellationRequested))
      {
        Console.WriteLine("Waiting for Holodeck before reconnecting to retained work.");
        await Task.Delay(TimeSpan.FromSeconds(retrySeconds), token);
      }
    }
  }

  private static T Read<T>(string path)
  {
    return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) ?? throw new IOException("A configuration file is empty.");
  }

  private static string Required(string name)
  {
    return Environment.GetEnvironmentVariable(name) ?? throw new IOException("Missing " + name);
  }

  private static void Validate(AgentConfiguration settings, RuntimeOptions options)
  {
    bool validRequests = settings.Model.TimeoutSeconds > 0 && settings.Embedding.TimeoutSeconds > 0 && settings.Model.MaxOutputTokens > 0 && settings.Embedding.Dimensions > 0;
    bool validLoop = options.PollSeconds > 0 && options.PollSeconds <= 60 && options.DatabaseTimeoutSeconds > options.PollSeconds && options.MaxToolRounds > 0;
    bool validContext = options.Context.MaxCharacters > options.OutputReserveCharacters && options.KeepRecentMessages > 0 && options.KeepRecentMessages < options.Context.RecentMessages && options.MaxReferenceEdge > 0 && options.PreviewTimeoutSeconds > 0 && options.ReferenceDescriptionCharacters > 0;
    bool validStorage = options.MaxNotebookCharacters > 0 && options.MaxPromptCharacters > 0 && options.SearchResults > 0 && options.ReconcileSeconds > 0 && options.RenderWaitSeconds > options.ReconcileSeconds;
    if (!validRequests || !validLoop || !validContext || !validStorage)
    {
      throw new IOException("The library's runtime or connection allowances are invalid.");
    }
  }

  private static void Cancel(PosixSignalContext context, CancellationTokenSource stop)
  {
    context.Cancel = true;
    stop.Cancel();
  }
}
