namespace OpenUL.Assets;

public interface IAssetLoader<out TAsset>
{
  static abstract TAsset Load(Stream stream);
}

public interface IAssetLoader<out TAsset, in TOptions>
{
  static abstract TAsset Load(Stream stream, TOptions options);
}