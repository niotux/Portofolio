namespace QuantiX.Engine.Assets;

public interface IAssetsProvider
{
  void CreateFile(string path);
  void DeleteFile(string path);
  Stream GetFile(string path, FileMode mode);
}
