using System.Numerics;
using System.Runtime.InteropServices;

namespace OpenUL.Assets.Media.Common;

public class PixelImage<TPixel> : Image, IImage<TPixel>
  where TPixel : unmanaged, IPixel
{
  public PixelImage(byte[] data, int width, int height)
  : base(data, width, height, TPixel.Channels, TPixel.ChannelFormat)
  { }

  public ref TPixel this[int x, int y]
  {
    get
    {
      if ((uint)x >= (uint)Size.Width || (uint)y >= (uint)Size.Height)
      {
        throw new IndexOutOfRangeException();
      }
      int index = y * Size.Width + x;
      var pixels = MemoryMarshal.Cast<byte, TPixel>(Data.AsSpan());
      return ref pixels[index];
    }
  }

  public static PixelImage<TPixel> Create(int width, int height)
    => new(new byte[width * height * TPixel.Channels * TPixel.ChannelFormat.GetByteSize()], width, height);

}