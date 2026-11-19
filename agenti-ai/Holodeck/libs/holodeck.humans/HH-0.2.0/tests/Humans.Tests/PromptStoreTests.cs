using Npgsql;
using Xunit;

namespace Humans.Tests;

public sealed class PromptStoreTests
{
  private static readonly CancellationToken Token = CancellationToken.None;

  [Fact]
  public void InvalidLabelsAndRemovedLabelsAreRejected()
  {
    PromptBundle bundle = TestPrompts.Bundle("continue");
    Assert.Throws<ArgumentException>(() => bundle.Require(["missing.txt"]));
    Assert.Throws<ArgumentException>(() => PromptBundle.ValidateFile("page.json", "{}"));
    Assert.Throws<ArgumentException>(() => PromptBundle.ValidateFile("page.json", "{\"Header\": \" \"}"));
    Assert.Throws<ArgumentException>(() => PromptBundle.ValidateReplacement("page.json", "{\"A\": \"a\", \"B\": \"b\"}", "{\"A\": \"a\"}"));
    PromptBundle.ValidateReplacement("page.json", "{\"A\": \"a\"}", "{\"A\": \"changed\", \"C\": \"new\"}");
    Assert.Throws<ArgumentException>(() => PromptBundle.ValidateFile("../look.txt", "test"));
    Assert.Throws<ArgumentException>(() => PromptBundle.ValidateFile("look.txt", " "));
  }

  [PromptDatabaseFact]
  public async Task EditsAreLiveAtOncePublicationsAreImmutableAndRestoreBringsOneBack()
  {
    PromptDatabaseHandler handler = await PromptDatabaseHandler.CreateAsync();
    try
    {
      PromptStore store = handler.Store();
      PromptStore otherClient = handler.Store();
      await store.CreateAsync("HH-0.1.1", TestPrompts.Bundle("original"), "Imported", Token);
      PromptLive start = await otherClient.RequireLiveAsync("HH-0.1.1", Token);
      Assert.Equal(1, start.Publication);

      long revision = await store.EditAsync("HH-0.1.1", "continue.txt", "trial", Token);
      PromptLive edited = await otherClient.RequireLiveAsync("HH-0.1.1", Token);
      Assert.Equal("trial", edited.Content.Get("continue.txt"));
      Assert.Equal(revision, edited.Revision);
      Assert.Null(edited.Publication);
      Assert.Equal(1, edited.Latest);

      Assert.Equal(2, await store.PublishAsync("HH-0.1.1", "Trial works", Token));
      await Assert.ThrowsAsync<InvalidOperationException>(() => store.PublishAsync("HH-0.1.1", "Again", Token));
      await store.EditAsync("HH-0.1.1", "continue.txt", "worse", Token);
      await store.RestoreAsync("HH-0.1.1", 2, Token);
      PromptLive restored = await store.RequireLiveAsync("HH-0.1.1", Token);
      Assert.Equal("trial", restored.Content.Get("continue.txt"));
      Assert.Equal(2, restored.Publication);
      Assert.Equal("original", (await store.PublicationAsync("HH-0.1.1", 1, Token)).Content.Get("continue.txt"));
      Assert.Equal(2, (await store.PublicationsAsync("HH-0.1.1", Token)).Count);

      await Assert.ThrowsAsync<PostgresException>(() => handler.ExecuteAsync("UPDATE hh_prompt_v1_publications SET label = 'changed'"));
      await Assert.ThrowsAsync<PostgresException>(() => handler.ExecuteAsync("DELETE FROM hh_prompt_v1_publications"));
    }
    finally
    {
      await handler.DeleteAsync();
    }
  }

  [PromptDatabaseFact]
  public async Task BrokenEditsNeverGoLiveAndVersionsStayApart()
  {
    PromptDatabaseHandler handler = await PromptDatabaseHandler.CreateAsync();
    try
    {
      PromptStore store = handler.Store();
      await store.CreateAsync("HH-0.1.1", TestPrompts.Bundle("original"), "Imported", Token);
      await Assert.ThrowsAsync<ArgumentException>(() => store.EditAsync("HH-0.1.1", "page.json", "{}", Token));
      await Assert.ThrowsAsync<ArgumentException>(() => store.EditAsync("HH-0.1.1", "page.json", "{\"Header\": \"only one\"}", Token));
      await Assert.ThrowsAsync<ArgumentException>(() => store.EditAsync("HH-0.1.1", "../escape.txt", "x", Token));
      Assert.Equal(1, (await store.RequireLiveAsync("HH-0.1.1", Token)).Revision);
      await Assert.ThrowsAsync<InvalidOperationException>(() => store.CreateAsync("HH-0.1.1", TestPrompts.Bundle("again"), "Again", Token));

      await store.EditAsync("HH-0.1.1", "continue.txt", "unpublished", Token);
      await store.StartFromAsync("HH-0.1.2", "HH-0.1.1", Token);
      PromptLive next = await store.RequireLiveAsync("HH-0.1.2", Token);
      Assert.Equal("original", next.Content.Get("continue.txt"));
      Assert.Equal("Started from HH-0.1.1 v1", (await store.PublicationAsync("HH-0.1.2", 1, Token)).Label);
      await store.EditAsync("HH-0.1.2", "continue.txt", "only in the newer version", Token);
      await store.EditAsync("HH-0.1.2", "added.txt", "a prompt only the newer version reads", Token);
      await store.RemoveAsync("HH-0.1.2", "added.txt", Token);
      await Assert.ThrowsAsync<KeyNotFoundException>(() => store.RemoveAsync("HH-0.1.2", "added.txt", Token));
      Assert.Equal("unpublished", (await store.RequireLiveAsync("HH-0.1.1", Token)).Content.Get("continue.txt"));
      Assert.Equal(2, (await store.VersionsAsync(Token)).Count);
      await Assert.ThrowsAsync<KeyNotFoundException>(() => store.RequireLiveAsync("HH-0.9.0", Token));
    }
    finally
    {
      await handler.DeleteAsync();
    }
  }
}
