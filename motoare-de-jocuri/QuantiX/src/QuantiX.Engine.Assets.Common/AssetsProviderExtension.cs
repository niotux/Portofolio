using Stb = StbImageSharp;
using StbWriter = StbImageWriteSharp;

namespace QuantiX.Engine.Assets.Common;

public static class AssetsProviderExtension
{
  public static Image LoadImageFile(this IAssetsProvider self, string path)
  {
    using var stream = self.GetFile(path, FileMode.Open);

    var result = Stb.ImageResult.FromStream(stream);
    uint channels = result.SourceComp switch
    {
      Stb.ColorComponents.Grey => 1,
      Stb.ColorComponents.GreyAlpha => 2,
      Stb.ColorComponents.RedGreenBlue => 3,
      Stb.ColorComponents.RedGreenBlueAlpha => 4,
      _ => throw new InvalidDataException(),
    };

    return new Image(result.Data, (uint)result.Width, (uint)result.Height, channels);
  }

  public static void SaveImageFile(this IAssetsProvider self, string path, Image image, ImageFileFormat fileFormat, int compressionQuality = 100)
  {
    using var stream = self.GetFile(path, FileMode.Open);

    StbWriter.ImageWriter writer = new();
    StbWriter.ColorComponents colors = image.ColorChannels switch
    {
      1 => StbWriter.ColorComponents.Grey,
      2 => StbWriter.ColorComponents.GreyAlpha,
      3 => StbWriter.ColorComponents.RedGreenBlue,
      4 => StbWriter.ColorComponents.RedGreenBlueAlpha,
      _ => throw new InvalidDataException(),
    };

    switch (fileFormat)
    {
      case ImageFileFormat.Png:
        writer.WritePng(image.Data, (int)image.Width, (int)image.Height, colors, stream);
        break;

      case ImageFileFormat.Jpg:
      case ImageFileFormat.Jpeg:
        writer.WriteJpg(image.Data, (int)image.Width, (int)image.Height, colors, stream, compressionQuality);
        break;

      case ImageFileFormat.Bmp:
        writer.WriteBmp(image.Data, (int)image.Width, (int)image.Height, colors, stream);
        break;

      default:
        throw new InvalidDataException();
    }
  }


  public static Image LoadImage(this IAssetsProvider self, string path)
  {
    var stream = self.GetFile(path, FileMode.Open);
    using BinaryReader reader = new(stream);

    uint width;
    uint height;
    uint colors;
    byte[] data;

    width = reader.ReadUInt32();
    height = reader.ReadUInt32();
    colors = reader.ReadUInt32();
    data = new byte[width * height * colors];
    reader.Read(data);

    return new Image(data, width, height, colors);
  }

  public static void SaveImage(this IAssetsProvider self, string path, Image image)
  {
    var stream = self.GetFile(path, FileMode.Open);
    using BinaryWriter writer = new(stream);

    writer.Write(image.Width);
    writer.Write(image.Height);
    writer.Write(image.ColorChannels);
    writer.Write(image.Data);
  }

  public static Audio LoadAudio(this IAssetsProvider self, string path)
  {
    byte[] bytes = self.LoadBytes(path);
    // Magic stuff

    throw new NotImplementedException();
  }

  public static Audio SaveAudio(this IAssetsProvider self, string path)
  {
    byte[] bytes = self.LoadBytes(path);
    // Magic stuff

    throw new NotImplementedException();
  }



  public static Video LoadVideo(this IAssetsProvider self, string path)
  {
    byte[] bytes = self.LoadBytes(path);
    // Magic stuff

    throw new NotImplementedException();
  }

  public static Video SaveVideo(this IAssetsProvider self, string path)
  {
    byte[] bytes = self.LoadBytes(path);
    // Magic stuff

    throw new NotImplementedException();
  }



  public static Font LoadFont(this IAssetsProvider self, string path)
  {
    byte[] bytes = self.LoadBytes(path);
    // Magic stuff

    throw new NotImplementedException();
  }

  public static Font SavwFont(this IAssetsProvider self, string path)
  {
    byte[] bytes = self.LoadBytes(path);
    // Magic stuff

    throw new NotImplementedException();
  }
}
