using Holodeck.Web.Projects;
using Xunit;

namespace Holodeck.Tests;

public sealed class ProjectStoreTests
{
  private static readonly CancellationToken Token = CancellationToken.None;

  [DatabaseFact]
  public async Task CreatesDefaultProjectAndListsFoldersAndProjects()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    ProjectStore store = new(database.Database);
    await store.InitializeAsync(Token);
    await store.InitializeAsync(Token);
    Folder folder = await store.CreateFolderAsync(new FolderInput("Stories", null), Token);
    Project project = await store.CreateAsync(new ProjectInput("Albert", "", folder.Id), Token);
    ProjectListing listing = await store.ListAsync(Token);
    Assert.Equal(folder, Assert.Single(listing.Folders));
    Assert.Equal(2, listing.Projects.Count);
    Project? fallback = await store.GetAsync(ProjectStore.DefaultProjectId, Token);
    Assert.NotNull(fallback);
    Assert.Equal("Default", fallback.Name);
    Assert.Equal(folder.Id, (await store.GetAsync(project.Id, Token))!.FolderId);
  }

  [DatabaseFact]
  public async Task UpdatesAndDeletesProjects()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    ProjectStore store = new(database.Database);
    await store.InitializeAsync(Token);
    Folder folder = await store.CreateFolderAsync(new FolderInput("Stories", null), Token);
    Project project = await store.CreateAsync(new ProjectInput("  Albert  ", null, folder.Id), Token);
    Assert.Equal("Albert", project.Name);
    await store.UpdateAsync(project.Id, new ProjectInput("Albert's Life", "A story.", null), Token);
    Project updated = (await store.GetAsync(project.Id, Token))!;
    Assert.Equal("Albert's Life", updated.Name);
    Assert.Equal("A story.", updated.Description);
    Assert.Null(updated.FolderId);
    await store.DeleteAsync(project.Id, Token);
    Assert.Null(await store.GetAsync(project.Id, Token));
    await Assert.ThrowsAsync<InvalidOperationException>(() => store.DeleteAsync(ProjectStore.DefaultProjectId, Token));
    await Assert.ThrowsAsync<KeyNotFoundException>(() => store.DeleteAsync("missing", Token));
    await Assert.ThrowsAsync<ArgumentException>(() => store.CreateAsync(new ProjectInput(" ", null, null), Token));
    await Assert.ThrowsAsync<ArgumentException>(() => store.CreateAsync(new ProjectInput(new string('a', 241), null, null), Token));
  }

  [DatabaseFact]
  public async Task DeletesProjectsThatHoldOldChats()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    ProjectStore store = new(database.Database);
    await store.InitializeAsync(Token);
    await database.ExecuteAsync("CREATE TABLE conversations (id text PRIMARY KEY, project_id text NOT NULL REFERENCES memory_projects(id), title text NOT NULL)");
    Project project = await store.CreateAsync(new ProjectInput("Old", null, null), Token);
    await database.ExecuteAsync($"INSERT INTO conversations VALUES ('c1', '{project.Id}', 'Chat')");
    await store.DeleteAsync(project.Id, Token);
    Assert.Equal(0, await database.CountAsync("SELECT count(*) FROM conversations"));
  }

  [DatabaseFact]
  public async Task KeepsExistingProjectRowsFromThePreviousSchema()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await database.ExecuteAsync("CREATE TABLE memory_projects (id text PRIMARY KEY, name text NOT NULL, description text NOT NULL, created_at timestamptz NOT NULL); INSERT INTO memory_projects VALUES ('p1', 'Albert', 'Kept', now())");
    ProjectStore store = new(database.Database);
    await store.InitializeAsync(Token);
    Project kept = (await store.GetAsync("p1", Token))!;
    Assert.Equal("Albert", kept.Name);
    Assert.Null(kept.FolderId);
  }

  [DatabaseFact]
  public async Task RenamesAndDeletesFolders()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    ProjectStore store = new(database.Database);
    await store.InitializeAsync(Token);
    Folder folder = await store.CreateFolderAsync(new FolderInput("Stories", null), Token);
    await store.UpdateFolderAsync(folder.Id, new FolderInput("Tales", null), Token);
    Assert.Equal("Tales", Assert.Single((await store.ListAsync(Token)).Folders).Name);
    await store.DeleteFolderAsync(folder.Id, Token);
    Assert.Empty((await store.ListAsync(Token)).Folders);
    await Assert.ThrowsAsync<KeyNotFoundException>(() => store.DeleteFolderAsync(folder.Id, Token));
  }

  [DatabaseFact]
  public async Task MovesFoldersButNotIntoThemselves()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    ProjectStore store = new(database.Database);
    await store.InitializeAsync(Token);
    Folder outer = await store.CreateFolderAsync(new FolderInput("Outer", null), Token);
    Folder inner = await store.CreateFolderAsync(new FolderInput("Inner", outer.Id), Token);
    Folder other = await store.CreateFolderAsync(new FolderInput("Other", null), Token);
    await Assert.ThrowsAsync<ArgumentException>(() => store.UpdateFolderAsync(outer.Id, new FolderInput("Outer", outer.Id), Token));
    await Assert.ThrowsAsync<ArgumentException>(() => store.UpdateFolderAsync(outer.Id, new FolderInput("Outer", inner.Id), Token));
    await store.UpdateFolderAsync(outer.Id, new FolderInput("Outer", other.Id), Token);
    await store.UpdateFolderAsync(inner.Id, new FolderInput("Inner", null), Token);
    List<Folder> folders = (await store.ListAsync(Token)).Folders;
    foreach (Folder folder in folders)
    {
      if (folder.Id == outer.Id)
      {
        Assert.Equal(other.Id, folder.ParentId);
      }

      if (folder.Id == inner.Id)
      {
        Assert.Null(folder.ParentId);
      }
    }
  }

  [DatabaseFact]
  public async Task DeletesFolderTreesAndKeepsTheDefaultProject()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    ProjectStore store = new(database.Database);
    await store.InitializeAsync(Token);
    Folder outer = await store.CreateFolderAsync(new FolderInput("Outer", null), Token);
    Folder inner = await store.CreateFolderAsync(new FolderInput("Inner", outer.Id), Token);
    Project nested = await store.CreateAsync(new ProjectInput("Nested", null, inner.Id), Token);
    await store.UpdateAsync(ProjectStore.DefaultProjectId, new ProjectInput("Default", null, inner.Id), Token);
    Assert.Equal(nested.Id, Assert.Single(await store.ListProjectsInFolderAsync(outer.Id, Token)));
    await store.DeleteAsync(nested.Id, Token);
    await store.DeleteFolderAsync(outer.Id, Token);
    ProjectListing listing = await store.ListAsync(Token);
    Assert.Empty(listing.Folders);
    Assert.Null(Assert.Single(listing.Projects).FolderId);
    await Assert.ThrowsAsync<KeyNotFoundException>(() => store.DeleteFolderAsync(outer.Id, Token));
  }
}
