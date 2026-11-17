using System.Runtime.CompilerServices;

namespace OpenUL.Assets.Media.Common;

public readonly struct ValueImage : IImage
{
  public ValueImage(byte[] data, int width, int height, byte channels, ImageChannelFormat channelFormat)
  {
    Data = data;
    Size = (width, height);
    Channels = channels;
    ChannelFormat = channelFormat;
  }

  public static ValueImage Create(int width, int height, byte channels, ImageChannelFormat channelFormat)
  => new(new byte[width * height * channels * channelFormat.GetByteSize()], width, height, channels, channelFormat);

  public static ValueImage Create(int width, int height, ImageChannels channels, ImageChannelFormat channelFormat)
    => new(new byte[width * height * channels.GetChannelCount() * channelFormat.GetByteSize()], width, height, channels.GetChannelCount(), channelFormat);


  public byte[] Data { get; }
  Memory<byte> IImage.Data => Data;

  public byte Channels { get; }
  public ImageChannelFormat ChannelFormat { get; }
  public (int Width, int Height) Size { get; }
  public (int X, int Y) Offset => (0, 0);

  public static implicit operator ReadOnlyValueImage(in ValueImage image)
    => new(image.Data, image.Size.Width, image.Size.Height, image.Channels, image.ChannelFormat);

  public Image ToImage()
  {
    return new(Data, Size.Width, Size.Height, Channels, ChannelFormat);
  }
}



public readonly struct ReadOnlyValueImage : IReadOnlyImage
{
  public ReadOnlyValueImage(ReadOnlyMemory<byte> data, int width, int height, byte channels, ImageChannelFormat channelFormat)
  {
    Data = data;
    Size = (width, height);
    Channels = channels;
    ChannelFormat = channelFormat;
  }

  public ReadOnlyMemory<byte> Data { get; }

  public byte Channels { get; }
  public ImageChannelFormat ChannelFormat { get; }
  public (int Width, int Height) Size { get; }
  public (int X, int Y) Offset => (0, 0);

  public IReadOnlyImage ToImage()
  {
    return this;
  }
}