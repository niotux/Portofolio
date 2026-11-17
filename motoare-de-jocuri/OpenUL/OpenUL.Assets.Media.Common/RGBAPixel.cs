using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;

namespace OpenUL.Assets.Media.Common;

[StructLayout(LayoutKind.Sequential)]
public struct RGBAPixel<T> : IPixel
  where T : unmanaged, INumber<T>
{
  public static ImageChannelFormat ChannelFormat => ImageChannelFormat.FromType<T>();
  public static byte Channels => 4;

  public T Red { get; set; }
  public T Green { get; set; }
  public T Blue { get; set; }
  public T Alpha { get; set; }
}