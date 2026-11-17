using System;

namespace QuantiX.Engine.Assets;

public class DiskAssets : IAssetsProvider
{
  private readonly string? baseFolderPath;
  public DiskAssets(string? baseFolderPath = null)
  {
    this.baseFolderPath = baseFolderPath;
    Directory.CreateDirectory(baseFolderPath ?? string.Empty);
  }

  private string GetPath(string path)
  {
    return this.baseFolderPath is null
      ? path
      : Path.Combine(this.baseFolderPath, path);
  }

  public void CreateFile(string path)
  {
    File.Create(GetPath(path)).Dispose();
  }

  public void DeleteFile(string path)
  {
    File.Delete(GetPath(path));
  }

  public Stream GetFile(string path, FileMode mode = FileMode.Open)
  {
    return File.Open(GetPath(path), mode);
  }
}
