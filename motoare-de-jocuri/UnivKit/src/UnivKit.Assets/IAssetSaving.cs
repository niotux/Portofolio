namespace UnivKit.Assets;

public interface IAssetSaving<in TSelf>
  where TSelf : IAssetSaving<TSelf>
{
  static abstract void SaveAsset(Stream stream, TSelf asset);
}

public interface IAssetSaving<in TSelf, in TOptions> : IAssetSaving<TSelf>
  where TSelf : IAssetSaving<TSelf, TOptions>
{
  static abstract void SaveAsset(Stream stream, TSelf asset, TOptions options);
}