using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PrimordialPower.Platform.Windows;

[SupportedOSPlatform("windows")]
internal static partial class Win32
{
  public static short LoWord(IntPtr value)
  {
    long intVal = value.ToInt64();
    short result = (short)(intVal & 0xFFFF);
    return result;
  }

  public static short HiWord(IntPtr value)
  {
    long intVal = value.ToInt64();
    short result = (short)((intVal >> 16) & 0xFFFF);
    return result;
  }



  [LibraryImport(LibNames.Kernel, StringMarshalling = StringMarshalling.Utf16)]
  public static partial IntPtr GetModuleHandleW(string? lpModuleName);


  [LibraryImport(LibNames.Kernel)]
  public static partial uint GetCurrentThreadId();


  [LibraryImport(LibNames.Kernel, SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  public static partial bool SetThreadPriority(IntPtr hThread, int nPriority);



  public const int THREAD_PRIORITY_TIME_CRITICAL = 15;
  public const int THREAD_PRIORITY_HIGHEST = 2;



  [LibraryImport(LibNames.Kernel, SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  public static partial bool CloseHandle(IntPtr hObject);


  [LibraryImport(LibNames.Kernel, SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
  public static partial IntPtr CreateEventW(
    IntPtr lpEventAttributes,
    [MarshalAs(UnmanagedType.Bool)] bool bManualReset,
    [MarshalAs(UnmanagedType.Bool)] bool bInitialState,
    string? lpName
  );


  [LibraryImport(LibNames.Kernel, SetLastError = true)]
  public static partial uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);



  public const uint WAIT_OBJECT_0 = 0;
  public const uint WAIT_TIMEOUT = 0x00000102;
  public const uint WAIT_FAILED = 0xFFFFFFFF;
  public const uint INFINITE = 0xFFFFFFFF;



  public static partial class COM
  {
    public const uint CLSCTX_ALL = 0x17;
    public const uint CLSCTX_INPROC_SERVER = 0x1;
    public const uint COINIT_MULTITHREADED = 0x0;



    [LibraryImport(LibNames.OLE32)]
    internal static partial int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

    [LibraryImport(LibNames.OLE32)]
    internal static partial void CoUninitialize();

    [LibraryImport(LibNames.OLE32)]
    internal static partial int CoCreateInstance(
      ref Guid rclsid,
      IntPtr pUnkOuter,
      uint dwClsContext,
      ref Guid riid,
      out IntPtr ppv
    );



    public static T CreateInstance<T>(Guid clsid)
    where T : class
    {
      Guid interfaceId = typeof(T).GUID;
      int hr = CoCreateInstance(
        rclsid: ref clsid,
        pUnkOuter: IntPtr.Zero,
        dwClsContext: COM.CLSCTX_ALL,
        riid: ref interfaceId,
        ppv: out IntPtr ptr
      );

      if (hr < 0) throw new Exception($"Failed to create COM object: 0x{hr:X}");

      T obj = (T)Marshal.GetObjectForIUnknown(ptr);
      Marshal.Release(ptr);

      return obj;
    }

    public static void Release(object obj)
    {
      Marshal.ReleaseComObject(obj);
    }

    public static void Init()
    {
      int hr = CoInitializeEx(IntPtr.Zero, COM.COINIT_MULTITHREADED);
      if (hr < 0 && hr != unchecked((int)0x80010106)) // Ignore RPC_E_CHANGED_MODE
        throw new Exception($"Failed to initialize COM: 0x{hr:X}");
    }

    public static void Uninit()
    {
      CoUninitialize();
    }
  }
}
