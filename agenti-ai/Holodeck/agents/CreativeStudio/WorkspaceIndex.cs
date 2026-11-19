namespace CreativeStudio;

// Rebuild the small recent-file index from already collected output files without re-running inference.
public static class WorkspaceIndex
{
  public static async Task RestoreAsync(CreativeStore store, WorkspaceFiles files, string workspace, int limit, CancellationToken token)
  {
    foreach (StoredGeneration generation in await store.ListGenerationsAsync(limit, token))
    {
      if (generation.State != "completed")
      {
        continue;
      }

      string directory = Path.Combine(workspace, "generations", generation.Id);
      if (!Directory.Exists(directory))
      {
        continue;
      }

      List<string> references = new();
      foreach (string path in Directory.EnumerateFiles(directory))
      {
        if (!WorkspaceFiles.IsImage(path))
        {
          continue;
        }

        string relative = Path.GetRelativePath(workspace, path);
        files.Resolve(relative);
        references.Add(relative);
      }

      await store.RecordGenerationFilesAsync(generation.Id, references, token);
    }
  }
}
