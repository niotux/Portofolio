using System.Diagnostics.CodeAnalysis;
using Stb = StbImageSharp;
using StbWriter = StbImageWriteSharp;

namespace UnivKit.Assets.Common;

public sealed class Image : ReadOnlyImage, IAssetLoading<Image>, IAssetSaving<Image, ImageFormat>
{
  public Image(byte[] data, uint width, uint height, uint channels) :
    base(data, width, height, channels)
  { }

  public new byte[] Data => base.data;



  public static new Image LoadAsset(Stream stream)
  {
    var result = Stb.ImageResult.FromStream(stream);
    uint channels = result.SourceComp switch
    {
      Stb.ColorComponents.Grey => 1,
      Stb.ColorComponents.GreyAlpha => 2,
      Stb.ColorComponents.RedGreenBlue => 3,
      Stb.ColorComponents.RedGreenBlueAlpha => 4,
      _ => throw new InvalidDataException(),
    };
    stream.Dispose();
    return new Image(result.Data, (uint)result.Width, (uint)result.Height, channels);
  }

  public static void SaveAsset(Stream stream, Image asset)
    => ReadOnlyImage.SaveAsset(stream, asset);

  public static void SaveAsset(Stream stream, Image asset, ImageFormat targetFormat)
    => ReadOnlyImage.SaveAsset(stream, asset, targetFormat);
}



public class ReadOnlyImage : IAssetLoading<ReadOnlyImage>, IAssetSaving<ReadOnlyImage, ImageFormat>
{
  public ReadOnlyImage(byte[] data, uint width, uint height, uint channels)
  {
    this.data = data;
    this.Width = width;
    this.Height = height;
    this.Channels = channels;
  }

  private protected readonly byte[] data;
  public byte[] Data => data;

  public uint Width { get; }
  public uint Height { get; }
  public uint Channels { get; }



  public static ReadOnlyImage LoadAsset(Stream stream)
    => Image.LoadAsset(stream);

  public static void SaveAsset(Stream stream, ReadOnlyImage asset)
    => SaveAsset(stream, asset, new ImageFormat() { FileFormat = ImageFileFormat.Png });

  public static void SaveAsset(Stream stream, ReadOnlyImage asset, ImageFormat targetFormat)
  {
    StbWriter.ImageWriter writer = new();
    StbWriter.ColorComponents colors = asset.Channels switch
    {
      1 => StbWriter.ColorComponents.Grey,
      2 => StbWriter.ColorComponents.GreyAlpha,
      3 => StbWriter.ColorComponents.RedGreenBlue,
      4 => StbWriter.ColorComponents.RedGreenBlueAlpha,
      _ => throw new InvalidDataException(),
    };

    switch (targetFormat.FileFormat)
    {
      case ImageFileFormat.Png:
        writer.WritePng(asset.Data, (int)asset.Width, (int)asset.Height, colors, stream);
        break;

      case ImageFileFormat.Jpg:
        writer.WriteJpg(asset.Data, (int)asset.Width, (int)asset.Height, colors, stream, targetFormat.CompressionQuality);
        break;

      case ImageFileFormat.Bmp:
        writer.WriteBmp(asset.Data, (int)asset.Width, (int)asset.Height, colors, stream);
        break;

      default:
        throw new InvalidDataException();
    }

    stream.Dispose();
  }
}


public enum ImageFileFormat
{
  Png,
  Jpg,
  Bmp,
}

public record struct ImageFormat
{
  public ImageFormat() { }

  public required ImageFileFormat FileFormat { get; init; }
  private int compressionQuality = 100;
  public int CompressionQuality
  {
    get => this.compressionQuality;
    init
    {
      if (value > 100 || value < 0)
      {
        throw new ArgumentOutOfRangeException
        (
          nameof(CompressionQuality),
          value,
          "Compression quality must be from 0 to 100."
        );
      }
      this.compressionQuality = value;
    }
  }
}