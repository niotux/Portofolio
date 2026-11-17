using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PrimordialPower.Platform.Win32;

[SupportedOSPlatform("windows")]
internal sealed partial class Win32WindowManagerServer
{
  private static class Def
  {
    public static readonly API.WNDCLASSW WindowClass = new()
    {
      Style = API.CS_HREDRAW | API.CS_VREDRAW,
      LPFNWndProc = WindowProc,
      LPSZClassName = "PrimordialPower.Window"
    };



    static Def()
    {
      ushort atom = API.RegisterClassW(ref WindowClass);
      if (atom == 0)
      {
        throw new Win32Exception(Marshal.GetLastWin32Error());
      }
    }



    private static IntPtr WindowProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
      Viewport? window = null;
      switch (msg)
      {
        case API.WM_CLOSE:
          if (!_openWindowsGlobal.TryGetValue(hWnd, out window)) return IntPtr.Zero;
          if (window.Core?.ConfirmDestroy() == true)
          {
            window.Destroy();
          }

          return IntPtr.Zero;


        case API.WM_USER_WINDOW_DESTROY:
          API.DestroyWindow(hWnd);
          return IntPtr.Zero;


        case API.WM_DESTROY:
          return IntPtr.Zero;


        case API.WM_SIZE:
          if (_openWindowsGlobal.TryGetValue(hWnd, out window))
          {
            int width = API.LoWord(lParam);
            int height = API.HiWord(lParam);
            window.Core?.UpdateInternalSize((uint)width, (uint)height);
          }
          return IntPtr.Zero;


        case API.WM_MOVE:
          if (_openWindowsGlobal.TryGetValue(hWnd, out window))
          {
            int x = API.LoWord(lParam);
            int y = API.HiWord(lParam);
            window.Core?.UpdateInternalPosition(x, y);
          }
          return IntPtr.Zero;


        case API.WM_SHOWWINDOW:
          if (_openWindowsGlobal.TryGetValue(hWnd, out window))
          {
            window.Core?.UpdateInternalActivation(true);
          }
          return IntPtr.Zero;


        case API.WM_SETFOCUS:
          if (_openWindowsGlobal.TryGetValue(hWnd, out window))
          {
            window.Core?.UpdateInternalActivation(true);
          }
          return IntPtr.Zero;


        case API.WM_KILLFOCUS:
          if (_openWindowsGlobal.TryGetValue(hWnd, out window))
          {
            window.Core?.UpdateInternalActivation(false);
          }
          return IntPtr.Zero;


        case API.WM_PAINT:
          API.ValidateRect(hWnd, IntPtr.Zero);
          if (_openWindowsGlobal.TryGetValue(hWnd, out window))
          {
            window.Core?.Update();
          }
          return IntPtr.Zero;


        case API.WM_SETCURSOR:
          switch (API.LoWord(lParam))
          {
            case API.HTCLIENT:
              API.SetCursor(API.LoadCursorW(IntPtr.Zero, API.IDC_ARROW));
              return (IntPtr)1;
            default:
              break;
          }
          break;

      }
      return API.DefWindowProcW(hWnd, msg, wParam, lParam);
    }
  }
}
