using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Holodeck.Web.Rooms;

// Opens a file on an agent's table so that the kernel refuses any path that leaves the table or passes through a
// symbolic link, including a file or folder the agent replaced after the path was checked. Uses Linux openat2(2)
// (kernel 5.6 or later); anywhere it is unavailable, agent files cannot be opened at all rather than opened unsafely.
internal static class TableFiles
{
  // Linux syscall and flag values; the same on x86-64 and arm64.
  private const long OpenAt2Syscall = 437;
  private const int CurrentDirectory = -100;
  private const ulong ReadOnly = 0;
  private const ulong NonBlocking = 0x800;
  private const ulong CloseOnExec = 0x80000;
  private const ulong PathOnly = 0x200000;
  private const ulong ResolveNoSymlinks = 0x04;
  private const ulong ResolveBeneath = 0x08;
  private const int NotPermitted = 1;
  private const int NotImplemented = 38;
  private const int BufferBytes = 81920;

  public static FileStream OpenRead(string table, string path, string displayName)
  {
    RequireSupportedPlatform();
    // The table itself is Holodeck's own path; only what lies beneath it is under the agent's control.
    using SafeFileHandle root = Open(CurrentDirectory, table, PathOnly | CloseOnExec, 0, displayName);
    // Non-blocking so a named pipe cannot stall the open; it does not affect reading a regular file.
    SafeFileHandle file = Open((int)root.DangerousGetHandle(), Path.GetRelativePath(table, path), ReadOnly | NonBlocking | CloseOnExec, ResolveBeneath | ResolveNoSymlinks, displayName);
    if ((File.GetAttributes(file) & FileAttributes.Directory) != 0)
    {
      file.Dispose();
      throw NotOnTable(displayName);
    }

    return new FileStream(file, FileAccess.Read, BufferBytes, false);
  }

  private static SafeFileHandle Open(int directory, string path, ulong flags, ulong resolve, string displayName)
  {
    OpenHow how = new() { Flags = flags, Mode = 0, Resolve = resolve };
    long descriptor = OpenAt2(OpenAt2Syscall, directory, path, ref how, (nuint)Marshal.SizeOf<OpenHow>());
    if (descriptor >= 0)
    {
      return new SafeFileHandle((IntPtr)descriptor, true);
    }

    int error = Marshal.GetLastPInvokeError();
    if (error == NotImplemented || error == NotPermitted)
    {
      throw new IOException("This system cannot open agent files safely: Linux openat2 is unavailable.");
    }

    throw NotOnTable(displayName);
  }

  private static void RequireSupportedPlatform()
  {
    Architecture architecture = RuntimeInformation.ProcessArchitecture;
    bool isSupported = OperatingSystem.IsLinux() && (architecture == Architecture.X64 || architecture == Architecture.Arm64);
    if (!isSupported)
    {
      throw new PlatformNotSupportedException("Agent files can only be opened safely on Linux (x86-64 or arm64).");
    }
  }

  private static ArgumentException NotOnTable(string displayName)
  {
    return new ArgumentException($"'{displayName}' is not a file on your table.");
  }

  [DllImport("libc", EntryPoint = "syscall", SetLastError = true)]
  private static extern long OpenAt2(long number, int directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, ref OpenHow how, nuint size);
}
