namespace OpenUL.Assets.Media.Common;

public readonly struct ImageSlice : IImage
{
  public ImageSlice(Memory<byte> data, int offX, int offY, int width, int height, byte channels, ImageChannelFormat channelFormat)
  {
    if (data.Length != width * height * channels * channelFormat.GetByteSize())
    {
      throw Image.GetLengthNotMatchingException();
    }

    Data = data;
    Size = (width, height);
    Offset = (offX, offY);
    Channels = channels;
    ChannelFormat = channelFormat;
  }


  public Memory<byte> Data { get; }

  public byte Channels { get; }
  public ImageChannelFormat ChannelFormat { get; }
  public (int Width, int Height) Size { get; }
  public (int X, int Y) Offset { get; }

  public static implicit operator ReadOnlyImageSlice(in ImageSlice slice)
  {
    return new(slice.Data, slice.Offset.X, slice.Offset.Y, slice.Size.Width, slice.Size.Height, slice.Channels, slice.ChannelFormat);
  }
}

public readonly struct ReadOnlyImageSlice : IReadOnlyImage
{
  public ReadOnlyImageSlice(ReadOnlyMemory<byte> data, int offX, int offY, int width, int height, byte channels, ImageChannelFormat channelFormat)
  {
    if (data.Length != width * height * channels * channelFormat.GetByteSize())
    {
      throw Image.GetLengthNotMatchingException();
    }

    Data = data;
    Size = (width, height);
    Offset = (offX, offY);
    Channels = channels;
    ChannelFormat = channelFormat;
  }

  public ReadOnlyMemory<byte> Data { get; }

  public byte Channels { get; }
  public ImageChannelFormat ChannelFormat { get; }
  public (int Width, int Height) Size { get; }
  public (int X, int Y) Offset { get; }
}