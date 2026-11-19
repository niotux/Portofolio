using Xunit;

namespace CreativeStudio.Tests;

public sealed class StudioInboxTests
{
  private static readonly CancellationToken Token = CancellationToken.None;

  [StoreDatabaseFact]
  public async Task AHandledRedeliveryIsNotReturnedForModelProcessing()
  {
    await using StoreDatabaseHandler database = await StoreDatabaseHandler.CreateAsync();
    using HttpClient databaseHttp = new(database, false);
    CreativeStore store = new(new HolodeckClient(databaseHttp, new Uri("http://holodeck.test/"), "key"));
    await store.InitializeAsync(3, Token);
    StoredReceipt receipt = new(31, "channel", "user", "You", "A saved discussion", new List<string>());
    await store.ReceiveAsync(receipt, Token);
    await store.CompleteTurnAsync(receipt, "Already answered.", new List<string>(), Token);
    using InboxTestHandler handler = new(new IncomingMessage(31, "channel", "user", "You", receipt.Note, receipt.Files));
    using HttpClient queueHttp = new(handler);
    StudioInbox inbox = new(new HolodeckClient(queueHttp, new Uri("http://holodeck.test/"), "key"), store, 1);

    Assert.Null(await inbox.NextAsync(Token));
    Assert.Equal(1, handler.Polls);
    Assert.Equal(2, (await store.RecentTurnsAsync("channel", 10, Token)).Count);
  }

  [StoreDatabaseFact]
  public async Task ADatabaseOutageDoesNotFetchAndAcknowledgeAnotherMessageBeforeSavingTheHeldOne()
  {
    await using StoreDatabaseHandler database = await StoreDatabaseHandler.CreateAsync();
    using HttpClient databaseHttp = new(database, false);
    CreativeStore store = new(new HolodeckClient(databaseHttp, new Uri("http://holodeck.test/"), "key"));
    await store.InitializeAsync(3, Token);
    IncomingMessage message = new(32, "channel", "user", "You", "A reference to retain", ["received/channel/You/reference.png"]);
    using InboxTestHandler handler = new(message, () => database.FailNextRequest = true);
    using HttpClient queueHttp = new(handler);
    StudioInbox inbox = new(new HolodeckClient(queueHttp, new Uri("http://holodeck.test/"), "key"), store, 1);

    await Assert.ThrowsAsync<HttpRequestException>(() => inbox.NextAsync(Token));
    StoredReceipt? restored = await inbox.NextAsync(Token);
    Assert.NotNull(restored);
    Assert.Equal(message.PacketId, restored.PacketId);
    Assert.Equal(message.Files, restored.Files);
    Assert.Equal(1, handler.Polls);
  }
}
