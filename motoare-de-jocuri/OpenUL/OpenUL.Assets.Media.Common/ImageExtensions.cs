using System.Runtime.CompilerServices;

namespace OpenUL.Assets.Media.Common;

public static class ImageExtensions
{
  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static ImageSlice Slice<TImage>(this TImage image, int offX, int offY, int width, int height)
    where TImage : IImage
  {
    bool offsetOut = (uint)offX > (uint)image.Size.Width || (uint)offY > (uint)image.Size.Height;
    bool sizeOut = (uint)width > (uint)(image.Size.Width - offX) || (uint)height > (uint)(image.Size.Height - offY);
    if (offsetOut || sizeOut)
    {
      throw new IndexOutOfRangeException();
    }
    return new(image.Data, offX, offY, width, height, image.Channels, image.ChannelFormat);
  }


  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static ImageSlice SliceOffset<TImage>(this TImage image, int offX, int offY)
    where TImage : IImage
    => Slice(image, offX, offY, image.Size.Width - offX, image.Size.Height - offY);


  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static ImageSlice SliceSize<TImage>(this TImage image, int width, int height)
    where TImage : IImage
    => Slice(image, 0, 0, width, height);



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static ReadOnlyImageSlice SliceReadOnly<TImage>(this TImage image, int offX, int offY, int width, int height)
  where TImage : IReadOnlyImage
  {
    bool offsetOut = (uint)offX > (uint)image.Size.Width || (uint)offY > (uint)image.Size.Height;
    bool sizeOut = (uint)width > (uint)(image.Size.Width - offX) || (uint)height > (uint)(image.Size.Height - offY);
    if (offsetOut || sizeOut)
    {
      throw new IndexOutOfRangeException();
    }
    return new(image.Data, offX, offY, width, height, image.Channels, image.ChannelFormat);
  }


  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static ReadOnlyImageSlice SliceOffsetReadOnly<TImage>(this TImage image, int offX, int offY)
    where TImage : IReadOnlyImage
    => SliceReadOnly(image, offX, offY, image.Size.Width - offX, image.Size.Height - offY);


  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static ReadOnlyImageSlice SliceSizeReadOnly<TImage>(this TImage image, int width, int height)
    where TImage : IReadOnlyImage
    => SliceReadOnly(image, 0, 0, width, height);
}