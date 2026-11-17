using System.Numerics;
using System.Runtime.CompilerServices;

namespace OpenUL.Assets.Media.Common;

public enum ImageChannelFormat : byte
{
  Byte = 1,
  Short = 2,
  Int = 4,
  Long = 8,
  Float = 9,
  Double = 13,
}

public static class ImageChannelFormatExt
{
  public static byte GetByteSize(this ImageChannelFormat format)
  {
    if (format < ImageChannelFormat.Float)
    {
      return (byte)format;
    }
    else
    {
      return (byte)(format - 5);
    }
  }

  extension(ImageChannelFormat fmt)
  {
    public static ImageChannelFormat FromType<T>()
      where T : unmanaged, INumber<T>
    {
      if (typeof(T) == typeof(float))
      {
        return ImageChannelFormat.Float;
      }
      if (typeof(T) == typeof(double))
      {
        return ImageChannelFormat.Double;
      }
      return (ImageChannelFormat)Unsafe.SizeOf<T>();
    }
  }
}