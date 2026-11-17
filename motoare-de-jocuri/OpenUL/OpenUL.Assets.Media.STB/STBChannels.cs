using OpenUL.Assets.Media.Common;
using Stb = StbImageSharp;

namespace OpenUL.Assets.Media.STB;

public enum STBChannelFormat : int
{
  Default = 0,
  Byte = 1,
  Short = 2,
  Float = 4,
}

public static class STBImageChannelSize
{
  public static ImageChannelFormat ToImageChannelFormat(this STBChannelFormat fmt)
  {
    if (fmt < STBChannelFormat.Float)
    {
      return (ImageChannelFormat)fmt;
    }
    return ImageChannelFormat.Float;
  }
}

public enum STBChannels : byte
{
  Default = Stb.StbImage.STBI_default,
  RGBA = Stb.StbImage.STBI_rgb_alpha,
  RGB = Stb.StbImage.STBI_rgb,
  GrayAlpha = Stb.StbImage.STBI_grey_alpha,
  Gray = Stb.StbImage.STBI_grey,
}