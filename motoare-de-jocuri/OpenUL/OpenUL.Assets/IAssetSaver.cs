namespace OpenUL.Assets;

public interface IAssetSaver<in TAsset>
{
  static abstract void Save(Stream stream, TAsset asset);
}

public interface IAssetSaver<in TAsset, TOptions>
{
  static abstract void Save(Stream stream, TAsset asset, TOptions options);
}