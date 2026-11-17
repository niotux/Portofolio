using System.Runtime.CompilerServices;

namespace OpenUL.Assets.Media.Common;

public class Image : IImage
{
  public Image(byte[] data, int width, int height, byte channels, ImageChannelFormat channelFormat)
  {
    if (data.Length != width * height * channels * channelFormat.GetByteSize())
    {
      throw GetLengthNotMatchingException();
    }

    Data = data;
    Size = (width, height);
    Channels = channels;
    ChannelFormat = channelFormat;
  }

  public static Image Create(int width, int height, byte channels, ImageChannelFormat channelFormat)
    => new(new byte[width * height * channels * channelFormat.GetByteSize()], width, height, channels, channelFormat);

  public static Image Create(int width, int height, ImageChannels channels, ImageChannelFormat channelFormat)
    => new(new byte[width * height * channels.GetChannelCount() * channelFormat.GetByteSize()], width, height, channels.GetChannelCount(), channelFormat);

  public byte[] Data { get; }
  Memory<byte> IImage.Data => Data;

  public byte Channels { get; }
  public ImageChannelFormat ChannelFormat { get; }
  public (int Width, int Height) Size { get; }
  public (int X, int Y) Offset => (0, 0);


  // [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  // public ImageSlice Slice(int offX, int offY, int width, int height)
  // {
  //   bool offsetOut = (uint)offX > (uint)Size.Width || (uint)offY > (uint)Size.Height;
  //   bool sizeOut = (uint)width > (uint)(Size.Width - offX) || (uint)height > (uint)(Size.Height - offY);
  //   if (offsetOut || sizeOut)
  //   {
  //     throw new IndexOutOfRangeException();
  //   }
  //   return new(Data, offX, offY, width, height, Channels, BytesPerChannel);
  // }

  // public ImageSlice SliceOffset(int offX, int offY)
  //   => Slice(offX, offY, Size.Width - offX, Size.Height - offY);

  // public ImageSlice SliceSize(int width, int height)
  //   => Slice(0, 0, width, height);

  public ValueImage ToValueImage()
  {
    return new(Data, Size.Width, Size.Height, Channels, ChannelFormat);
  }

  internal static InvalidDataException GetLengthNotMatchingException()
  {
    return new("Image data length must match the pixel format");
  }
}