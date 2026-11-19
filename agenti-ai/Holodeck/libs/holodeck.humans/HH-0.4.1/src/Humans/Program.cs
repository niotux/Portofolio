using System.Runtime.InteropServices;
using System.Text.Json;

namespace Humans;

public static class Program
{
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
  // Agent database calls run up to Holodeck's 30-second statement limit, plus the trip.
  private const int DatabaseSeconds = 45;
  private const string PromptLibraryId = "holodeck.humans";

  public static async Task<int> Main(string[] args)
  {
    using CancellationTokenSource stop = new();
    using PosixSignalRegistration terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context => Cancel(context, stop));
    using PosixSignalRegistration interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, context => Cancel(context, stop));
    try
    {
      string library = Required("HOLODECK_LIBRARY");
      string project = args.Length > 1 ? args[1] : library;
      AgentSettings settings = Read<AgentSettings>(Required("HOLODECK_SETTINGS"));
      RuntimeOptions options = Read<RuntimeOptions>(Path.Combine(project, "runtime.json"));
      EmbeddingOptions embedding = settings.Embedding ?? options.Embedding;
      Validate(settings, embedding, options);
      string workspace = Required("HOLODECK_TABLE");
      using HttpClient transportHttp = new() { Timeout = TimeSpan.FromSeconds(Body.ReceiveSeconds + 15) };
      using HttpClient chatHttp = new() { Timeout = Timeout.InfiniteTimeSpan };
      using HttpClient backgroundHttp = new() { Timeout = Timeout.InfiniteTimeSpan };
      using HttpClient embeddingHttp = new() { Timeout = Timeout.InfiniteTimeSpan };
      using HttpClient databaseHttp = new() { Timeout = TimeSpan.FromSeconds(DatabaseSeconds) };
      Uri holodeckUrl = new(Required("HOLODECK_URL").TrimEnd('/') + "/");
      string key = Required("HOLODECK_KEY");
      Holodeck transport = new(transportHttp, holodeckUrl, key);
      Holodeck activityTransport = new(new HttpClient { Timeout = TimeSpan.FromSeconds(15) }, holodeckUrl, key);
      Store store = new(new Holodeck(databaseHttp, holodeckUrl, key));
      Identity identity = await FirstContactAsync(transport, options, stop.Token);
      if (identity.Capabilities is null || !identity.Capabilities.Contains("idempotent-send-v1") || !identity.Capabilities.Contains("database-v1") || !identity.Capabilities.Contains("library-database-v1"))
      {
        throw new IOException("This agent needs Holodeck's replay-safe send, private database and library database support.");
      }
      LibraryDatabase shared = new(databaseHttp, new Uri(holodeckUrl, "api/agent/library-database/" + PromptLibraryId), key);
      LivePrompts prompts = new(new PromptStore(shared), ReadVersion(project));
      await store.InitializeAsync(embedding.Dimensions, stop.Token);
      string name = string.IsNullOrWhiteSpace(settings.Personality.Name) ? identity.Name : settings.Personality.Name;
      TimeZoneInfo zone = Zone(options.TimeZone);
      Page page = new(name, zone, options.TimeGapMinutes);
      Page memoryPage = new(name, zone, options.TimeGapMinutes);
      PromptLive initial = await prompts.ReadAsync(stop.Token);
      page.Use(initial);
      memoryPage.Use(initial);
      ChatModel model = new(chatHttp, settings.Model);
      Embedder embedder = new(embeddingHttp, embedding, page.QueryInstruction);
      Console.WriteLine("Humans " + initial.Version + ": one continuing person with a body loop, private thought, hands and semantic memory.");
      // Semantic memory is required. Do not silently substitute keyword search or let an unreachable service look like amnesia.
      await embedder.EmbedAsync(name, true, stop.Token);
      foreach (Memory memory in await store.NeedingEmbeddingAsync(embedder.Tag, stop.Token))
      {
        await store.ReembedAsync(memory.Id, await embedder.EmbedAsync(memory.Text, false, stop.Token), embedder.Tag, stop.Token);
      }
      Prepared prepared = await new Preparation(model, store, page, options).PortraitAsync(settings.Personality, stop.Token);
      page.Portrait = prepared.Portrait;
      memoryPage.Portrait = prepared.Portrait;
      if (prepared.Zone.Length > 0)
      {
        page.Zone = Zone(prepared.Zone);
        memoryPage.Zone = page.Zone;
      }
      Body body = new(transport, store, options);
      await body.RefreshAsync(page, stop.Token);
      Person person = new(model, new Hands(model, page, options), new Recall(model, embedder, store, page, options), store, body, page, options, prompts, activityTransport, workspace);
      await person.LoadAsync(stop.Token);
      MemoryKeeper keeper = new(new ChatModel(backgroundHttp, settings.Model), embedder, store, memoryPage, options, prompts, workspace);
      Task receiving = body.ReceiveAsync(page, stop.Token);
      Task forming = keeper.RunAsync(stop.Token);
      try
      {
        await person.RunAsync(stop.Token);
      }
      finally
      {
        await stop.CancelAsync();
        await Quietly(receiving);
        await Quietly(forming);
      }
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
      Console.WriteLine($"Agent stopped: {exception.Message}");
      Console.WriteLine("Configure the chat model and embedding endpoint in Settings; grant llama and memory in Computer. Existing workspace data was retained.");
      return 1;
    }
  }

  private static async Task Quietly(Task task)
  {
    try
    {
      await task;
    }
    catch (OperationCanceledException)
    {
    }
    catch (UnauthorizedAccessException)
    {
    }
  }

  private static TimeZoneInfo Zone(string id)
  {
    try
    {
      return TimeZoneInfo.FindSystemTimeZoneById(id);
    }
    catch (Exception exception) when (exception is TimeZoneNotFoundException || exception is InvalidTimeZoneException)
    {
      Console.WriteLine("The configured time zone is not available on this computer; the page shows UTC.");
      return TimeZoneInfo.Utc;
    }
  }

  // The HH project's VERSION names which shared prompt set this person runs.
  private static string ReadVersion(string project)
  {
    return HumanVersion.Require(File.ReadAllText(Path.Combine(project, "VERSION")).AsSpan().Trim().ToString());
  }

  // An agent may start before Holodeck is ready, or reconnect after its host returns.
  private static async Task<Identity> FirstContactAsync(Holodeck transport, RuntimeOptions options, CancellationToken token)
  {
    while (true)
    {
      try
      {
        return await transport.MeAsync(token);
      }
      catch (Exception exception) when (exception is HttpRequestException || (exception is OperationCanceledException && !token.IsCancellationRequested))
      {
        Console.WriteLine("Holodeck is not reachable yet; trying again.");
        await Task.Delay(TimeSpan.FromSeconds(options.PollSeconds), token);
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

  private static void Validate(AgentSettings settings, EmbeddingOptions embedding, RuntimeOptions options)
  {
    if (settings.Personality is null || settings.Model is null || string.IsNullOrWhiteSpace(settings.Model.BaseUrl) || string.IsNullOrWhiteSpace(settings.Model.Name))
    {
      throw new IOException("Set the chat model base URL and model name before starting.");
    }
    if (string.IsNullOrWhiteSpace(embedding.BaseUrl) || string.IsNullOrWhiteSpace(embedding.Name) || embedding.Dimensions < 1 || embedding.TimeoutSeconds < 1)
    {
      throw new IOException("Configure semantic memory's embedding endpoint, model and dimensions.");
    }
    bool validContext = options.ContextTokens > 0 && options.CompactPercent > 0 && options.CompactPercent < 100 && options.KeepEntries >= 4 && options.CharactersPerToken > 0;
    bool validMemory = options.PerCue > 0 && options.MaxLookups > 0 && options.QuietCycles >= 0 && options.MaxRecallCharacters > 0 && options.FormationAttempts > 0 && options.FormEntries > 0 && options.MinSimilarity > -1 && options.MinSimilarity < 1;
    bool validPasses = options.ThoughtTokens > 0 && options.SpeechTokens > 0 && options.UtilityTokens > 0 && options.ThoughtTemperature >= 0 && options.SpeechTemperature >= 0 && options.UtilityTemperature >= 0 && options.HandsTemperature >= 0;
    bool validLoop = options.PollSeconds > 0 && options.MaxCyclesPerWake > 0 && options.MaxCyclesPerHour > 0 && options.IdleWakeMinutes >= 0 && options.OpenMessages > 0 && options.HandsEntries > 0 && options.LookEntries > 0 && options.TimeGapMinutes > 0;
    bool validRequests = settings.Model.TimeoutSeconds > 0 && settings.Model.MaxOutputTokens > 0;
    if (!validContext || !validMemory || !validPasses || !validLoop || !validRequests)
    {
      throw new IOException("The library's runtime allowances or model request limits are invalid.");
    }
  }

  private static void Cancel(PosixSignalContext context, CancellationTokenSource stop)
  {
    context.Cancel = true;
    stop.Cancel();
  }
}
