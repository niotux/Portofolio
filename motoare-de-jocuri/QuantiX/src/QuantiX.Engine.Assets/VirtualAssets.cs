using System;

namespace QuantiX.Engine.Assets;

public class VirtualAssets : IAssetsProvider
{
  private readonly string dataFilePath;
  public VirtualAssets(string dataFilePath)
  {
    this.dataFilePath = dataFilePath;
  }

  public void CreateFile(string path)
  {
    throw new NotImplementedException();
  }

  public void DeleteFile(string path)
  {
    throw new NotImplementedException();
  }

  public Stream GetFile(string path, FileMode mode = FileMode.Open)
  {
    throw new NotImplementedException();
  }
}
