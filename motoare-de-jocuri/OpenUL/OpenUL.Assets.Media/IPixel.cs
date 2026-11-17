using OpenUL.Assets.Media.Common;

namespace OpenUL.Assets.Media;

public interface IPixel
{
  static abstract ImageChannelFormat ChannelFormat { get; }
  static abstract byte Channels { get; }
}