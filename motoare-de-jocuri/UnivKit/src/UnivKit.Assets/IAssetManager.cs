namespace UnivKit.Assets;

public interface IAssetManager : IDisposable
{
  TAsset LoadAsset<TAsset>(string path)
    where TAsset : IAssetLoading<TAsset>;

  TAsset LoadAsset<TAsset, TOptions>(string path, TOptions options)
    where TAsset : IAssetLoading<TAsset, TOptions>;

  void SaveAsset<TAsset>(string path, TAsset asset)
    where TAsset : IAssetSaving<TAsset>;

  void SaveAsset<TAsset, TOptions>(string path, TAsset asset, TOptions options)
    where TAsset : IAssetSaving<TAsset, TOptions>;
}