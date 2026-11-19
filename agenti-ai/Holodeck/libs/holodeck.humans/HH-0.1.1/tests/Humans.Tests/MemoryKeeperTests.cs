using Xunit;

namespace Humans.Tests;

public sealed class MemoryKeeperTests
{
  [Fact]
  public async Task QueuePollingSurvivesHostTransportOutage()
  {
    using FormationOutageHandler handler = new();
    using HttpClient http = new(handler);
    MemoryStore store = new(new Holodeck(http, new Uri("http://holodeck.invalid/"), "session"));
    EmbeddingOptions embedding = new("http://memory.invalid/v1/", "encoder", 2, 30);
    RuntimeOptions options = new(6, 2, 75, 8192, 4, 6, 8, 16000, 1, 3, embedding, 200, 0.5, 0.6, 0.3);
    ChatModel model = new(http, new ModelOptions("http://model.invalid/v1/", "test", "", 0.5, 100, 30));
    using StringWriter log = new();
    MemoryKeeper keeper = new(model, new Embedder(http, embedding, ""), store, new Page(new Personality("Writer", ""), "Writer", "User", ""), options, log);
    using CancellationTokenSource stop = new();
    Task running = keeper.RunAsync(stop.Token);
    try
    {
      await handler.Recovered.WaitAsync(TimeSpan.FromSeconds(5));
      Assert.False(running.IsCompleted);
      Assert.Contains("waiting for its services", log.ToString());
    }
    finally
    {
      await stop.CancelAsync();
      await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
    }
  }
}
