using System.Runtime.InteropServices;

namespace Holodeck.Web.Rooms;

// Linux struct open_how for openat2(2): flags, mode, and resolve, each a 64-bit field.
[StructLayout(LayoutKind.Sequential)]
internal struct OpenHow
{
  public ulong Flags;
  public ulong Mode;
  public ulong Resolve;
}
