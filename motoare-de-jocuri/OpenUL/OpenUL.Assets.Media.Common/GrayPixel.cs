using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;

namespace OpenUL.Assets.Media.Common;

[StructLayout(LayoutKind.Sequential)]
public struct GrayPixel<T> : IPixel
  where T : unmanaged, INumber<T>
{
  public static ImageChannelFormat ChannelFormat => ImageChannelFormat.FromType<T>();
  public static byte Channels => 1;

  public T Alpha { get; set; }
}