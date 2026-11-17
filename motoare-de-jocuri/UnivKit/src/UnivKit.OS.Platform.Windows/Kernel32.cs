using System.Runtime.InteropServices;

namespace UnivKit.OS.Platform.Windows;

public static partial class Kernel32
{
  public const string LibName = "kernel32.dll";



  [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf16)]
  public static partial IntPtr GetModuleHandleW(string? lpModuleName);


  [LibraryImport(LibName)]
  public static partial uint GetCurrentThreadId();


  [LibraryImport(LibName)]
  public static partial IntPtr GetCurrentThread();


  [LibraryImport(LibName, SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  public static partial bool SetThreadPriority(IntPtr hThread, ThreadPriority nPriority);


  [LibraryImport(LibName, SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  public static partial bool CloseHandle(IntPtr hObject);


  [LibraryImport(LibName, SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
  public static partial IntPtr CreateEventW
  (
    IntPtr lpEventAttributes,
    [MarshalAs(UnmanagedType.Bool)] bool bManualReset,
    [MarshalAs(UnmanagedType.Bool)] bool bInitialState,
    string? lpName
  );


  [LibraryImport(LibName, SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  public static partial bool SetEvent(IntPtr hEvent);


  [LibraryImport(LibName, SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  public static partial bool ResetEvent(IntPtr hEvent);


  [LibraryImport(LibName, SetLastError = true)]
  public static partial uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);


  [LibraryImport(LibName, SetLastError = true)]
  public static partial uint WaitForMultipleObjects
  (
    uint nCount,
    IntPtr[] lpHandles,
    [MarshalAs(UnmanagedType.Bool)] bool bWaitAll,
    uint dwMilliseconds
  );


  [LibraryImport(LibName)]
  [return: MarshalAs(UnmanagedType.Bool)]
  public static partial bool QueryPerformanceCounter(out long lpPerformanceCount);


  [LibraryImport(LibName)]
  [return: MarshalAs(UnmanagedType.Bool)]
  public static partial bool QueryPerformanceFrequency(out long lpFrequency);


  [LibraryImport(LibName, SetLastError = true)]
  public static partial nuint SetThreadAffinityMask(IntPtr hThread, nuint dwThreadAffinityMask);


  public enum ThreadPriority : int
  {
    IDLE = -15,
    LOWEST = -2,
    BELOW_NORMAL = -1,
    NORMAL = 0,
    ABOVE_NORMAL = 1,
    HIGHEST = 2,
    TIME_CRITICAL = 15,
  }


  public enum WaitResult : uint
  {
    OBJECT_0 = 0x00000000,
    TIMEOUT = 0x00000102,
    FAILED = 0xFFFFFFFF,
  }


  public const uint INFINITE = 0xFFFFFFFF;
}
