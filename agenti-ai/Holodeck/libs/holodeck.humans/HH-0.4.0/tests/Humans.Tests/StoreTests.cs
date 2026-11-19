using Xunit;

namespace Humans.Tests;

// The person's own storage: receipts, the continuing history, deliveries and the memory queue survive and never repeat.
public sealed class StoreTests
{
  private static readonly CancellationToken Token = CancellationToken.None;

  [PromptDatabaseFact]
  public async Task ReceiptsBuzzOnceAndReadingShowsOnlyWhatIsNew()
  {
    PromptDatabaseHandler handler = await PromptDatabaseHandler.CreateAsync();
    try
    {
      Store store = new(handler.Agent());
      await store.InitializeAsync(4, Token);
      Delivery first = new(10, "a", "Ana", "ana-id", "Ana", "Mara?", new List<string>());
      Assert.True(await store.ReceiveAsync(first, true, Token));
      Assert.False(await store.ReceiveAsync(first, true, Token));
      await store.ReceiveAsync(new Delivery(11, "b", "group", "bo-id", "Bo", "lunch?", new List<string>()), false, Token);
      List<Buzz> buzzes = await store.TakeBuzzesAsync(["b"], Token);
      Assert.Single(buzzes);
      Assert.True(buzzes[0].Mention);
      await store.ReceiveAsync(new Delivery(12, "b", "group", "bo-id", "Bo", "anyone?", new List<string>()), false, Token);
      await store.SetMutedAsync("b", false, Token);
      Assert.Empty(await store.MutedAsync(Token));
      List<Buzz> unmuted = await store.TakeBuzzesAsync(await store.MutedAsync(Token), Token);
      Assert.Equal("b", unmuted[0].Channel);
      Assert.False(unmuted[0].Mention);
      Assert.Empty(await store.TakeBuzzesAsync([], Token));
      List<Unread> unread = await store.UnreadAsync(Token);
      Assert.Equal(2, unread.Count);
      Assert.Equal(["bo-id"], unread[1].Senders);
      List<Received> read = await store.ReadAsync("a", 15, Token);
      Assert.Equal("Mara?", read[0].Note);
      Assert.Empty(await store.ReadAsync("a", 15, Token));
      Assert.Equal(2, await store.UnseenCountAsync("b", Token));
    }
    finally
    {
      await handler.DeleteAsync();
    }
  }

  [PromptDatabaseFact]
  public async Task WhatIsSaidIsQueuedOnceAndTheHistoryResumesAfterItsSummary()
  {
    PromptDatabaseHandler handler = await PromptDatabaseHandler.CreateAsync();
    try
    {
      Store store = new(handler.Agent());
      await store.InitializeAsync(4, Token);
      Entry thought = await store.AppendAsync(EntryKinds.Thought, "", "", "I should answer.", Token);
      Entry said = await store.SayAsync("a", "Mara", "Hi Ana.", Token);
      PendingSend? send = await store.NextSendAsync(Token);
      Assert.Equal(said.Id, send!.Id);
      Assert.Equal("Hi Ana.", send.Text);
      await store.SentAsync(send.Id, Token);
      Assert.Null(await store.NextSendAsync(Token));

      await store.SaveSummaryAsync(new Summary(thought.Id, "I meant to answer Ana."), Token);
      History history = await store.HistoryAsync(Token);
      Assert.Equal("I meant to answer Ana.", history.Summary!.Text);
      Assert.Single(history.Entries);
      Assert.Equal(EntryKinds.Said, history.Entries[0].Kind);
      await store.SetStateAsync("focus", "a", Token);
      Assert.Equal("a", await store.StateAsync("focus", Token));
      await store.SaveContactAsync("you-id", "Alex", Token);
      Assert.Equal("Alex", (await store.ContactsAsync(Token))["you-id"]);
    }
    finally
    {
      await handler.DeleteAsync();
    }
  }

  [PromptDatabaseFact]
  public async Task AStretchBecomesMemoryOnceAndRecallLeavesOutWhatIsStillLive()
  {
    PromptDatabaseHandler handler = await PromptDatabaseHandler.CreateAsync();
    try
    {
      Store store = new(handler.Agent());
      await store.InitializeAsync(4, Token);
      await store.QueueFormationAsync(1, 5, "What happened: Ana asked about the boat.", Token);
      await store.QueueFormationAsync(1, 5, "again", Token);
      Assert.Equal(5, await store.LastFormedAsync(Token));
      Formation form = (await store.ClaimFormationAsync(3, Token))!;
      await store.SaveDraftAsync(form.Id, "Ana asked me about the boat.", Token);
      await store.KeepAsync(form, ["Ana asked me about the boat.", "I felt pleased."], [[1, 0, 0, 0], [0, 1, 0, 0]], "tag", Token);
      Assert.Null(await store.ClaimFormationAsync(3, Token));
      Assert.Equal(2, await store.MemoryCountAsync(Token));
      Assert.Equal(2, (await store.NearestAsync([1, 0, 0, 0], "tag", 6, [], 4, Token)).Count);
      Assert.Empty(await store.NearestAsync([1, 0, 0, 0], "tag", 5, [], 4, Token));

      Assert.Null(await store.NextAlarmAsync(Token));
      await store.SetAlarmAsync(DateTimeOffset.UtcNow.AddMinutes(-1), "call mum", Token);
      await store.SetAlarmAsync(DateTimeOffset.UtcNow.AddHours(2), "later", Token);
      Assert.NotNull(await store.NextAlarmAsync(Token));
      Assert.Equal(["call mum"], await store.TakeRungAlarmsAsync(Token));
      Assert.Empty(await store.TakeRungAlarmsAsync(Token));
      Assert.Equal("", await store.MindAsync(Token));
      await store.SaveMindAsync("Waiting for Ana.", Token);
      await store.SaveMindAsync("Ana answered; nothing open.", Token);
      Assert.Equal("Ana answered; nothing open.", await store.MindAsync(Token));
      Assert.Null(await store.PreparedAsync("p", Token));
      await store.SavePreparedAsync(new Prepared("p", "I am Mara."), Token);
      Assert.Equal("I am Mara.", (await store.PreparedAsync("p", Token))!.Portrait);
    }
    finally
    {
      await handler.DeleteAsync();
    }
  }
}
