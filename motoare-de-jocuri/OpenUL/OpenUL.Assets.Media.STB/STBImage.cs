using System.Runtime.InteropServices;
using OpenUL.Assets.Media.Common;
using Stb = StbImageSharp;
using StbWrite = StbImageWriteSharp;

namespace OpenUL.Assets.Media.STB;


public sealed class STBImage
: IAssetLoader<Image>,
  IAssetLoader<Image, STBLoadOptions>,
  IAssetSaver<Image>,
  IAssetSaver<Image, STBSaveOptions>
{
  private STBImage() { }

  public static Image Load(Stream stream)
  {
    return Load(stream, STBLoadOptions.Default);
  }

  public static Image Load(Stream stream, STBLoadOptions options)
  {
    var imageData = LoadCore(stream, options);
    return imageData.ToImage();
  }



  public static void Save(Stream stream, Image asset)
  {
    Save(stream, asset, STBSaveOptions.Default);
  }

  public static void Save(Stream stream, Image asset, STBSaveOptions options)
  {
    SaveCore(stream, asset.ToValueImage(), options);
  }



  internal static unsafe void SaveCore(Stream stream, in ValueImage image, in STBSaveOptions options)
  {
    if (image.ChannelFormat != ImageChannelFormat.Byte)
    {
      throw new NotSupportedException("Image must have only 1 byte per channel. STB only supports 1 byte channels");
    }
    void callback(void* context, void* data, int size)
    {
      var span = new ReadOnlySpan<byte>(data, size);
      stream.Write(span);
    }

    fixed (byte* src = image.Data)
    {
      (int width, int height) = image.Size;
      int channels = image.Channels;
      int stride = image.Size.Width * image.Channels * image.ChannelFormat.GetByteSize();

      int ok = options.Format switch
      {
        STBFileFormat.PNG => StbWrite.StbImageWrite.stbi_write_png_to_func(callback, null, width, height, channels, src, stride),
        STBFileFormat.BMP => StbWrite.StbImageWrite.stbi_write_bmp_to_func(callback, null, width, height, channels, src),
        STBFileFormat.TGA => StbWrite.StbImageWrite.stbi_write_tga_to_func(callback, null, width, height, channels, src),
        STBFileFormat.JPG => StbWrite.StbImageWrite.stbi_write_jpg_to_func(callback, null, width, height, channels, src, options.Quality.Percentage),
        STBFileFormat.HDR => StbWrite.StbImageWrite.stbi_write_hdr_to_func(callback, null, width, height, channels, (float*)src),
        _ => throw new InvalidOperationException("Unsupported STB write format"),
      };

      if (ok == 0)
      {
        throw new InvalidOperationException("STB image writer failed to encode image");
      }
    }
  }

  internal static unsafe ValueImage LoadCore(Stream stream, in STBLoadOptions options)
  {
    var context = new Stb.StbImage.stbi__context(stream);

    int width = 0;
    int height = 0;
    int fileChannels = 0;
    int channels = (int)options.Channels;

    byte* result = options.ChannelFormat switch
    {
      STBChannelFormat.Byte => Stb.StbImage.stbi__load_and_postprocess_8bit(context, &width, &height, &fileChannels, channels),
      STBChannelFormat.Short => (byte*)Stb.StbImage.stbi__load_and_postprocess_16bit(context, &width, &height, &fileChannels, channels),
      STBChannelFormat.Float => (byte*)Stb.StbImage.stbi__loadf_main(context, &width, &height, &fileChannels, channels),
      _ => throw new InvalidOperationException("STB image loader only supports byte, short and float channel types"),
    };

    if (channels == Stb.StbImage.STBI_default)
    {
      channels = fileChannels;
    }
    if (result == null)
    {
      throw new InvalidOperationException(Stb.StbImage.stbi__g_failure_reason);
    }

    var pixels = new byte[width * height * channels * (int)options.ChannelFormat];
    fixed (byte* dst = pixels) Buffer.MemoryCopy(result, dst, pixels.Length, pixels.Length);
    Marshal.FreeHGlobal((nint)result);

    return new(pixels, width, height, (byte)channels, options.ChannelFormat.ToImageChannelFormat());
  }
}

