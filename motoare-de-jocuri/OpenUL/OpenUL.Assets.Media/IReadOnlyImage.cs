using OpenUL.Assets.Media.Common;

namespace OpenUL.Assets.Media;

public interface IReadOnlyImage
{
  byte Channels { get; }
  ImageChannelFormat ChannelFormat { get; }

  (int Width, int Height) Size { get; }
  (int X, int Y) Offset { get; }
  ReadOnlyMemory<byte> Data { get; }
}

public interface IReadOnlyImage<TPixel> : IReadOnlyImage
  where TPixel : IPixel
{
  byte IReadOnlyImage.Channels => TPixel.Channels;
  ImageChannelFormat IReadOnlyImage.ChannelFormat => TPixel.ChannelFormat;

  TPixel this[int x, int y] { get; }
}