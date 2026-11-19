using System.Text.Json;
using Npgsql;
using Xunit;

namespace CreativeStudio.Tests;

public sealed class CreativeStoreTests
{
  private static readonly CancellationToken Token = CancellationToken.None;

  [StoreDatabaseFact]
  public async Task ReceiptsFilesAndSentRepliesSurviveStoreRestartWithoutResending()
  {
    await using StoreDatabaseHandler database = await StoreDatabaseHandler.CreateAsync();
    using HttpClient http = new(database, false);
    HolodeckClient transport = new(http, new Uri("http://holodeck.test/"), "test");
    CreativeStore first = new(transport);
    await first.InitializeAsync(3, Token);
    StoredReceipt receipt = new(41, "channel", "writer", "You", "Keep this reference", ["received/channel/You/reference.png"]);
    Assert.True(await first.ReceiveAsync(receipt, Token));
    Assert.False(await first.ReceiveAsync(receipt, Token));

    CreativeStore restored = new(transport);
    StoredReceipt pending = (await restored.PendingReceiptAsync(Token))!;
    Assert.Equal(receipt.PacketId, pending.PacketId);
    Assert.Equal(receipt.Files, pending.Files);
    Assert.Equal(receipt.Files[0], Assert.Single(await restored.RecentReferencesAsync("channel", 3, 200, Token)).Path);
    Assert.Empty(await restored.RecentReferencesAsync("another-channel", 3, 200, Token));
    await restored.CompleteTurnAsync(pending, "I will keep it.", new List<string>(), Token);
    StoredSend send = (await restored.NextSendAsync(Token))!;
    await restored.SentAsync(send.Key, Token);
    await restored.CompleteTurnAsync(pending, "A different regenerated reply", new List<string>(), Token);
    Assert.Null(await restored.PendingReceiptAsync(Token));
    Assert.Null(await restored.NextSendAsync(Token));
    List<StoredTurn> turns = await restored.RecentTurnsAsync("channel", 10, Token);
    Assert.Equal("I will keep it.", turns[1].Text);
    await restored.SaveBriefAsync("channel", "The user shared a reference.", turns[0].Position, Token);
    Assert.Equal("assistant", Assert.Single(await restored.RecentTurnsAsync("channel", 10, Token)).Role);
  }

  [StoreDatabaseFact]
  public async Task NotebookKeepsDescriptiveRevisionsAndConversationScope()
  {
    await using StoreDatabaseHandler database = await StoreDatabaseHandler.CreateAsync();
    using HttpClient http = new(database, false);
    CreativeStore store = new(new HolodeckClient(http, new Uri("http://holodeck.test/"), "test"));
    await store.InitializeAsync(3, Token);
    NotebookWrite write = new("channel", "The station", "The enormous windows establish the cold empty atmosphere.", ["received/channel/You/station.png"], [1, 0, 0], "embedding-a", 1, "station");
    NotebookNote first = await store.SaveNoteAsync(write, Token);
    NotebookNote revised = await store.SaveNoteAsync(write with { Prose = "Keep the enormous windows, but use warm lighting." }, Token);
    Assert.NotEqual(first.Revision, revised.Revision);
    NotebookNote found = Assert.Single(await store.SearchNotesAsync("channel", [1, 0, 0], "embedding-a", 3, Token));
    Assert.Equal(revised.Prose, found.Prose);
    Assert.Equal(write.Files, found.Files);
    Assert.Empty(await store.SearchNotesAsync("private-other-channel", [1, 0, 0], "embedding-a", 3, Token));
    Assert.Empty(await store.SearchNotesAsync("channel", [1, 0, 0], "different-embedding-model", 3, Token));
    await Assert.ThrowsAsync<IOException>(() => store.SaveNoteAsync(write with { Channel = "private-other-channel", Prose = "overwrite" }, Token));
    Assert.Equal(revised.Prose, (await store.GetNoteAsync("channel", "station", Token))!.Prose);
  }

  [StoreDatabaseFact]
  public async Task PublishedPromptSelectionSurvivesNewLibraryDefaultsAndSupportsRollback()
  {
    await using StoreDatabaseHandler database = await StoreDatabaseHandler.CreateAsync();
    using HttpClient http = new(database, false);
    CreativeStore store = new(new HolodeckClient(http, new Uri("http://holodeck.test/"), "test"));
    await store.InitializeAsync(3, Token);
    await store.SeedPromptAsync("conversation", "Library first", Token);
    PromptRevision original = await store.ActivePromptAsync("conversation", Token);
    PromptRevision draft = await store.SavePromptAsync("conversation", "My tuned version", Token);
    Assert.Equal(original.Revision, (await store.ActivePromptAsync("conversation", Token)).Revision);
    await store.SelectPromptAsync("conversation", draft.Revision, Token);
    await store.SeedPromptAsync("conversation", "Library replacement", Token);
    Assert.Equal(draft.Revision, (await store.ActivePromptAsync("conversation", Token)).Revision);
    await store.SelectPromptAsync("conversation", original.Revision, Token);
    Assert.Equal("Library first", (await store.ActivePromptAsync("conversation", Token)).Content);
    Assert.Equal(3, (await store.PromptVersionsAsync("conversation", Token)).Count);
    await store.SeedPromptAsync("conversation", "Another library default", Token);
    Assert.Equal(original.Revision, (await store.ActivePromptAsync("conversation", Token)).Revision);
    await Assert.ThrowsAsync<KeyNotFoundException>(() => store.SelectPromptAsync("conversation", "missing", Token));
  }

  [StoreDatabaseFact]
  public async Task GenerationSchedulingAndReviewRetainTheOriginalRequestAndDoNotDuplicateDelivery()
  {
    await using StoreDatabaseHandler database = await StoreDatabaseHandler.CreateAsync();
    using HttpClient http = new(database, false);
    CreativeStore store = new(new HolodeckClient(http, new Uri("http://holodeck.test/"), "test"));
    await store.InitializeAsync(3, Token);
    StoredReceipt receipt = new(51, "channel", "writer", "You", "Generate the station", new List<string>());
    await store.ReceiveAsync(receipt, Token);
    StoredGeneration generation = new("job", receipt.PacketId, receipt.ChannelId, "{\"prompt\":\"warm station\",\"seed\":42}", "prepared", null, null);
    await store.ScheduleGenerationAsync(generation, receipt, "I will render it.", Token);
    Assert.Null(await store.PendingReceiptAsync(Token));
    Assert.Equal(generation.Id, (await store.PendingGenerationAsync(Token))!.Id);
    StoredSend planning = (await store.NextSendAsync(Token))!;
    await store.SentAsync(planning.Key, Token);

    await store.UpdateGenerationAsync("job", "submitted", "external-job", null, Token);
    await Assert.ThrowsAsync<IOException>(() => store.UpdateGenerationAsync("job", "submitted", "different-renderer-job", null, Token));
    await store.ReviewGenerationAsync("job", "I could not confirm the renderer's response.", "connection lost", Token);
    Assert.Null(await store.PendingGenerationAsync(Token));
    await store.ResumeGenerationAsync("job", Token);
    StoredGeneration resumed = (await store.PendingGenerationAsync(Token))!;
    Assert.Equal("external-job", resumed.ExternalId);
    Assert.Equal("submitted", resumed.State);
    await Assert.ThrowsAsync<IOException>(() => store.SaveGenerationAsync(generation with { RequestJson = "{\"prompt\":\"changed\"}" }, Token));
    StoredSend notice = (await store.NextSendAsync(Token))!;
    await store.SentAsync(notice.Key, Token);
    await store.CompleteGenerationAsync("job", "Here is the station.", ["generated/station.png"], Token);
    StoredSend result = (await store.NextSendAsync(Token))!;
    Assert.Equal("generated/station.png", Assert.Single(result.Files));
    await store.SentAsync(result.Key, Token);
    await store.CompleteGenerationAsync("job", "Changed replay", ["generated/other.png"], Token);
    Assert.Null(await store.NextSendAsync(Token));
    Assert.Null(await store.PendingGenerationAsync(Token));
    Assert.Equal("completed", (await store.GenerationAsync("job", Token))!.State);
    Assert.Equal("generated/station.png", Assert.Single(await store.RecentReferencesAsync("channel", 3, 200, Token)).Path);
  }
  [StoreDatabaseFact]
  public async Task FailedSchedulingDoesNotAcknowledgeTheMessageOrQueueAReply()
  {
    await using StoreDatabaseHandler database = await StoreDatabaseHandler.CreateAsync();
    using HttpClient http = new(database, false);
    CreativeStore store = new(new HolodeckClient(http, new Uri("http://holodeck.test/"), "test"));
    await store.InitializeAsync(3, Token);
    StoredReceipt receipt = new(71, "channel", "writer", "You", "Generate it", new List<string>());
    await store.ReceiveAsync(receipt, Token);
    StoredGeneration invalid = new("invalid", receipt.PacketId, receipt.ChannelId, "not-json", "prepared", null, null);
    await Assert.ThrowsAsync<PostgresException>(() => store.ScheduleGenerationAsync(invalid, receipt, "I will render it.", Token));
    Assert.Equal(receipt.PacketId, (await store.PendingReceiptAsync(Token))!.PacketId);
    Assert.Null(await store.GenerationAsync("invalid", Token));
    Assert.Null(await store.NextSendAsync(Token));
  }

  [StoreDatabaseFact]
  public async Task PromptRegistryInitializationCanRetryAfterTheHostDisconnects()
  {
    await using StoreDatabaseHandler database = await StoreDatabaseHandler.CreateAsync();
    using HttpClient http = new(database, false);
    CreativeStore store = new(new HolodeckClient(http, new Uri("http://holodeck.test/"), "test"));
    await store.InitializeAsync(3, Token);
    string library = Path.Combine(Path.GetTempPath(), "creative-prompts-" + Guid.NewGuid().ToString("N"));
    string directory = Path.Combine(library, "prompts");
    Directory.CreateDirectory(directory);
    try
    {
      PromptManifest manifest = new([new PromptDefinition("conversation", "conversation.txt", "Conversation")]);
      await File.WriteAllTextAsync(Path.Combine(directory, "registry.json"), JsonSerializer.Serialize(manifest), Token);
      await File.WriteAllTextAsync(Path.Combine(directory, "conversation.txt"), "Editable conversation instructions", Token);
      PromptRegistry registry = new(library, store, 1000);
      database.FailNextRequest = true;
      await Assert.ThrowsAsync<HttpRequestException>(() => registry.InitializeAsync(Token));
      await registry.InitializeAsync(Token);
      Assert.Equal("Editable conversation instructions", (await registry.GetAsync("conversation", Token)).Content);
    }
    finally
    {
      Directory.Delete(library, true);
    }
  }

}
