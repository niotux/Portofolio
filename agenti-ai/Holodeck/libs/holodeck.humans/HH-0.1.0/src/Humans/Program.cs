using System.Runtime.InteropServices;
using System.Text.Json;

namespace Humans;

public static class Program
{
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
  // Agent database calls run up to Holodeck's 30-second statement limit, plus the trip.
  private const int DatabaseSeconds = 45;

  public static async Task<int> Main(string[] args)
  {
    using CancellationTokenSource stop = new();
    using PosixSignalRegistration terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context => Cancel(context, stop));
    using PosixSignalRegistration interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, context => Cancel(context, stop));
    try
    {
      string library = Required("HOLODECK_LIBRARY");
      string personLibrary = args.Length > 1 ? args[1] : library;
      AgentSettings settings = Read<AgentSettings>(Required("HOLODECK_SETTINGS"));
      RuntimeOptions options = Read<RuntimeOptions>(Path.Combine(personLibrary, "runtime.json"));
      EmbeddingOptions embedding = settings.Embedding ?? options.Embedding;
      Validate(settings, embedding, options);
      string workspace = Required("HOLODECK_TABLE");
      using HttpClient transportHttp = new() { Timeout = TimeSpan.FromSeconds(options.PollSeconds + 15) };
      using HttpClient chatHttp = new() { Timeout = Timeout.InfiniteTimeSpan };
      using HttpClient backgroundHttp = new() { Timeout = Timeout.InfiniteTimeSpan };
      using HttpClient embeddingHttp = new() { Timeout = Timeout.InfiniteTimeSpan };
      using HttpClient databaseHttp = new() { Timeout = TimeSpan.FromSeconds(DatabaseSeconds) };
      Uri holodeckUrl = new(Required("HOLODECK_URL").TrimEnd('/') + "/");
      string key = Required("HOLODECK_KEY");
      Holodeck transport = new(transportHttp, holodeckUrl, key);
      MemoryStore store = new(new Holodeck(databaseHttp, holodeckUrl, key));
      Identity identity = await FirstContactAsync(transport, options, stop.Token);
      if (identity.Capabilities is null || !identity.Capabilities.Contains("idempotent-send-v1") || !identity.Capabilities.Contains("database-v1"))
      {
        throw new IOException("This agent needs Holodeck's replay-safe send and agent database support.");
      }
      await store.InitializeAsync(embedding.Dimensions, stop.Token);
      Page page = new(Path.Combine(personLibrary, "prompts"), settings.Personality, identity.Name, ReadRoleText(library, args, "other.txt", "someone"), ReadRoleText(library, args, "self.txt", ""));
      ChatModel model = new(chatHttp, settings.Model);
      Embedder embedder = new(embeddingHttp, embedding, page.QueryInstruction);
      Console.WriteLine("Base Person: a page of chat and private thoughts continued by the model; backstory and exchanges in semantic memory.");
      // Semantic memory is required. Do not silently substitute keyword search or let an unreachable service look like amnesia.
      await embedder.EmbedAsync(identity.Name, true, stop.Token);
      foreach (Memory memory in await store.NeedingEmbeddingAsync(embedder.Tag, stop.Token))
      {
        await store.ReembedAsync(memory.Id, await embedder.EmbedAsync(memory.Text, false, stop.Token), embedder.Tag, stop.Token);
      }
      await SeedAsync(store, embedder, ReadBackstory(library, args), stop.Token);
      Self self = new(model, embedder, store, page, options, workspace, options.ContextTokens, transport);
      MemoryKeeper keeper = new(new ChatModel(backgroundHttp, settings.Model), embedder, store, page, options, Console.Out, transport);
      Task forming = keeper.RunAsync(stop.Token);
      try
      {
        await RunAsync(transport, identity, store, self, forming, options, stop.Token);
      }
      finally
      {
        await stop.CancelAsync();
        try
        {
          await forming;
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
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

  private static async Task RunAsync(Holodeck transport,Identity identity, MemoryStore store, Self self, Task forming, RuntimeOptions options, CancellationToken token)
  {
    while (!token.IsCancellationRequested)
    {
      if (forming.IsCompleted)
      {
        await forming;
      }
      try
      {
        await FlushAsync(transport, store, token);
        Delivery? waiting = await store.PendingAsync(token);
        Delivery? packet = await transport.NextAsync(waiting is null ? options.PollSeconds : 0, token);
        if (packet is not null)
        {
          identity = await transport.MeAsync(token);
          bool human = IsHuman(identity, packet.SenderId);
          await store.ReceiveAsync(packet, human, token);
          // Receipt is durable before the following /next acknowledges it.
        }
        waiting = await store.PendingAsync(token);
        if (waiting is not null)
        {
          identity = await transport.MeAsync(token);
          if (!HasChannel(identity, waiting.ChannelId))
          {
            // Keep the receipt, but do not let work for a removed channel block the current channels.
            await store.HeardWithoutReplyAsync(waiting.PacketId, token);
            continue;
          }
          ResponseResult? result = await self.SayAsync(waiting, token);
          await FlushAsync(transport, store, token);
          if (result is not null)
          {
            await self.CompactIfFullAsync(result, token);
          }
        }
      }
      catch (Exception exception) when (exception is HttpRequestException || exception is IOException || (exception is OperationCanceledException && !token.IsCancellationRequested))
      {
        Console.WriteLine("A model, embedding or transport request could not finish: " + exception.Message + " Retrying the saved work; check the endpoint settings.");
        await Task.Delay(TimeSpan.FromSeconds(options.PollSeconds), token);
      }
    }
  }

  private static async Task FlushAsync(Holodeck transport, MemoryStore store, CancellationToken token)
  {
    PendingSend? send;
    while ((send = await store.NextSendAsync(token)) is not null)
    {
      Identity identity = await transport.MeAsync(token);
      if (HasChannel(identity, send.Channel))
      {
        await transport.SendAsync(send, token);
      }
      // The exchange stays in memory even when its channel has been removed and its reply cannot be delivered.
      await store.SentAsync(send.PacketId, token);
    }
  }

  private static bool HasChannel(Identity identity, string channelId)
  {
    foreach (AgentChannel channel in identity.Channels)
    {
      if (channel.ChannelId == channelId)
      {
        return true;
      }
    }
    return false;
  }

  private static bool IsHuman(Identity identity, string sender)
  {
    foreach (AgentChannel channel in identity.Channels)
    {
      foreach (Participant participant in channel.Participants)
      {
        if (participant.MemberId == sender)
        {
          return participant.IsYou;
        }
      }
    }
    return false;
  }

  // A library agent's backstory (its first argument names it) is first-person memories under roles/<name>/memories.txt,
  // one per paragraph. They come back only when something calls them, like any other memory.
  private static List<string> ReadBackstory(string library, string[] args)
  {
    List<string> memories = new();
    if (args.Length == 0)
    {
      return memories;
    }
    string path = Path.Combine(library, "roles", args[0], "memories.txt");
    if (!File.Exists(path))
    {
      return memories;
    }
    foreach (string paragraph in File.ReadAllText(path).Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
      memories.Add(paragraph);
    }
    return memories;
  }

  private static string ReadRoleText(string library, string[] args, string file, string fallback)
  {
    string path = args.Length == 0 ? "" : Path.Combine(library, "roles", args[0], file);
    return path.Length > 0 && File.Exists(path) ? File.ReadAllText(path).Trim() : fallback;
  }

  private static async Task SeedAsync(MemoryStore store, Embedder embedder, List<string> backstory, CancellationToken token)
  {
    List<string> kept = await store.SeedsAsync(token);
    bool same = kept.Count == backstory.Count;
    for (int i = 0; same && i < kept.Count; i++)
    {
      same = kept[i] == backstory[i];
    }
    if (same)
    {
      return;
    }
    List<float[]> vectors = new();
    foreach (string memory in backstory)
    {
      vectors.Add(await embedder.EmbedAsync(memory, false, token));
    }
    await store.ReplaceSeedsAsync(backstory, vectors, embedder.Tag, token);
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
    bool validContext = options.KeepTurns >= 2 && options.RecentTurns >= 2 && options.CompactPercent > 0 && options.CompactPercent < 100 && options.ContextTokens > 0;
    bool validMemory = options.PerCue > 0 && options.QuietTurns >= 0 && options.KnownMemories >= 0 && options.MaxRecallCharacters > 0 && options.FormationAttempts > 0 && options.ThoughtTokens > 0 && options.MinSimilarity > -1 && options.MinSimilarity < 1 && options.SpeechTemperature >= 0 && options.UtilityTemperature >= 0;
    bool validRequests = options.PollSeconds > 0 && options.PollSeconds <= 60 && settings.Model.TimeoutSeconds > 0 && settings.Model.MaxOutputTokens > 0;
    if (!validContext || !validMemory || !validRequests)
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
