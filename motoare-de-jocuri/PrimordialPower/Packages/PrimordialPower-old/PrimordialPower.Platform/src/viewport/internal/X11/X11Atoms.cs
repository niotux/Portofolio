using System;

namespace PrimordialPower.Platform.X11;

internal struct X11Atoms
{
  public X11Atoms(IntPtr display)
  {
    this.WMNullAtom = API.XInternAtom(display, API.WM_USER_NULL, API.Bool32.False);
    this.WMDeleteAtom = API.XInternAtom(display, API.WM_DELETE_WINDOW, API.Bool32.False);
    this.WMInitWindowsAtom = API.XInternAtom(display, API.WM_USER_WINDOW_INIT, API.Bool32.False);
    this.WMQuitAtom = API.XInternAtom(display, API.WM_USER_QUIT, API.Bool32.False);
    this.WMProtocolAtom = API.XInternAtom(display, API.WM_PROTOCOLS, API.Bool32.False);
    this.WMDestroyWindowAtom = API.XInternAtom(display, API.WM_USER_DESTROY_WINDOW, API.Bool32.False);
    this.WMFocusWindowAtom = API.XInternAtom(display, API.WM_USER_FOCUS_WINDOW, API.Bool32.False);
  }

  public UIntPtr WMNullAtom;
  public UIntPtr WMDeleteAtom;
  public UIntPtr WMInitWindowsAtom;
  public UIntPtr WMQuitAtom;
  public UIntPtr WMProtocolAtom;
  public UIntPtr WMDestroyWindowAtom;
  public UIntPtr WMFocusWindowAtom;
}
