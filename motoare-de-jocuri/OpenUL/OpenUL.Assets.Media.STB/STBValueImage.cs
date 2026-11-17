using OpenUL.Assets.Media.Common;

namespace OpenUL.Assets.Media.STB;


public sealed class STBValueImage
: IAssetLoader<ValueImage>,
  IAssetLoader<ValueImage, STBLoadOptions>,
  IAssetSaver<ValueImage>,
  IAssetSaver<ValueImage, STBSaveOptions>
{
  private STBValueImage() { }

  public static ValueImage Load(Stream stream)
  {
    return Load(stream, STBLoadOptions.Default);
  }

  public static ValueImage Load(Stream stream, STBLoadOptions options)
  {
    var imageData = STBImage.LoadCore(stream, options);
    return imageData;
  }



  public static void Save(Stream stream, ValueImage asset)
  {
    Save(stream, asset, STBSaveOptions.Default);
  }

  public static void Save(Stream stream, ValueImage asset, STBSaveOptions options)
  {
    STBImage.SaveCore(stream, asset, options);
  }
}

