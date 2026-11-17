namespace OpenUL.Assets.Media.Common;

public enum ImageChannels : byte
{
  Gray = 1,
  GrayAlpha = 2,
  RGB = 3,
  RGBA = 4,
}

public static class ImageChannelsExt
{
  public static byte GetChannelCount(this ImageChannels channels)
  {
    return (byte)channels;
  }
}