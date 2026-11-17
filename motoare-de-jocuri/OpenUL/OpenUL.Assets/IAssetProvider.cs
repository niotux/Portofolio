namespace OpenUL.Assets;

public interface IAssetProvider<TLookup> : IDisposable
{
  TAsset LoadAsset<TAsset>(TLookup path)
    where TAsset : IAssetLoader<TAsset>;

  TAsset LoadAsset<TAsset, TOptions>(TLookup path, TOptions options)
    where TAsset : IAssetLoader<TAsset, TOptions>;

  void SaveAsset<TAsset>(TLookup path, TAsset asset)
    where TAsset : IAssetSaver<TAsset>;

  void SaveAsset<TAsset, TOptions>(TLookup path, TAsset asset, TOptions options)
    where TAsset : IAssetSaver<TAsset, TOptions>;
}