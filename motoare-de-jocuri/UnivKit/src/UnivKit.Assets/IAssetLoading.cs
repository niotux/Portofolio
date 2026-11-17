namespace UnivKit.Assets;

public interface IAssetLoading<out TSelf>
  where TSelf : IAssetLoading<TSelf>
{
  static abstract TSelf LoadAsset(Stream stream);
}

public interface IAssetLoading<out TSelf, in TOptions> : IAssetLoading<TSelf>
  where TSelf : IAssetLoading<TSelf, TOptions>
{
  static abstract TSelf LoadAsset(Stream stream, TOptions options);
}