using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OpenUL.Windowing.Desktop;
using OpenUL.Windowing.Events;
using Lib = Silk.NET.GLFW;

namespace OpenUL.Windowing.GLFW;

public class GLFWWindow : IDesktopWindow
{
  private readonly GLFWWindowingBackend Backend;
  private readonly Lib.Glfw Glfw;
  internal unsafe GLFWWindow(GLFWWindowingBackend backend, Lib.Glfw glfw, Lib.WindowHandle* handle, in DesktopWindowProps props)
  {
    Backend = backend;
    AtomicHandlePtr = (nint)handle;
    Glfw = glfw;

    Handle = backend.CreateWindowHandle(handle);

    AtomicSize.LoWord = props.Size.Width;
    AtomicSize.HiWord = props.Size.Height;
    if (props.Position.HasValue)
    {
      AtomicPosition.LoWord = props.Position.Value.X;
      AtomicPosition.HiWord = props.Position.Value.Y;
    }
    else
    {
      Glfw.GetWindowPos(HandlePtr, out AtomicPosition.LoWord, out AtomicPosition.HiWord);
    }

    AtomicTitle = props.Title;
    AtomicIsVisible = props.Visible;
    AtomicIsFocused = props.Focused;

    IsResizeable = props.Resizable;

    Glfw.SetWindowCloseCallback(HandlePtr, OnClosing);
    Glfw.SetWindowPosCallback(HandlePtr, OnMoved);
    Glfw.SetWindowSizeCallback(HandlePtr, OnResized);
    Glfw.SetWindowFocusCallback(HandlePtr, OnFocused);
    Glfw.SetWindowIconifyCallback(HandlePtr, OnMinimize);
    Glfw.SetWindowMaximizeCallback(HandlePtr, OnMaximize);
    Glfw.SetWindowRefreshCallback(HandlePtr, OnRefresh);
  }

  private AtomicDWord32 AtomicSize;
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private void SetAtomicSize((int Width, int Height) value)
  {
    AtomicDWord32 word = new()
    {
      LoWord = value.Width,
      HiWord = value.Height,
    };
    Interlocked.Exchange(ref AtomicSize.Data, word.Data);
  }
  public (int Width, int Height) Size
  {
    get
    {
      AtomicDWord32 copy = default;
      Interlocked.Exchange(ref copy.Data, AtomicSize.Data);
      return (copy.LoWord, copy.HiWord);
    }
    set
    {
      if (!IsOpen)
      {
        return;
      }

      SetAtomicSize(value);
      unsafe { Glfw.SetWindowSize(HandlePtr, value.Width, value.Height); }
    }
  }

  private AtomicDWord32 AtomicPosition;
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private void SetAtomicPosition((int X, int Y) value)
  {
    AtomicDWord32 word = new()
    {
      LoWord = value.X,
      HiWord = value.Y,
    };
    Interlocked.Exchange(ref AtomicPosition.Data, word.Data);
  }
  public (int X, int Y) Position
  {
    get
    {
      AtomicDWord32 copy = default;
      Interlocked.Exchange(ref copy.Data, AtomicPosition.Data);
      return (copy.LoWord, copy.HiWord);
    }
    set
    {
      if (!IsOpen)
      {
        return;
      }

      SetAtomicPosition(value);
      unsafe { Glfw.SetWindowPos(HandlePtr, value.X, value.Y); }
    }
  }

  [StructLayout(LayoutKind.Explicit)]
  private struct AtomicDWord32
  {
    [FieldOffset(0)]
    public ulong Data;


    [FieldOffset(0)]
    public int LoWord;


    [FieldOffset(sizeof(int))]
    public int HiWord;
  }


  private string AtomicTitle;
  public string Title
  {
    get
    {
      string result = null!;
      Interlocked.Exchange(ref result, AtomicTitle);
      return result;
    }
    set
    {
      if (!IsOpen)
      {
        return;
      }

      Interlocked.Exchange(ref AtomicTitle, value);
      unsafe { Glfw.SetWindowTitle(HandlePtr, value); }

      TitleChanged?.Invoke(value);

      var e = Event;
      if (e != null)
      {
        Backend.TitleEvent.Title = value;
        e(Backend.TitleEvent);
      }
    }
  }

  private volatile nint AtomicHandlePtr;
  private unsafe Lib.WindowHandle* HandlePtr => (Lib.WindowHandle*)AtomicHandlePtr;
  public WindowHandle Handle { get; }

  private volatile bool AtomicIsVisible;
  public bool IsVisible => AtomicIsVisible;

  private volatile bool AtomicIsFocused;
  public bool IsFocused => AtomicIsFocused;

  public bool IsOpen => AtomicHandlePtr != 0;

  public bool IsResizeable { get; private init; }



  public event Action<WindowEventArgs>? Event;
  public event Action<EventArgs>? OtherEvent;
  public event WindowResizedEventHandler? Resized;
  public event WindowVisibilityEventHandler? VisibilityChanged;
  public event WindowFocusEventHandler? FocusChanged;
  public event WindowUpdateEventHandler? Updated;
  public event WindowClosingEventHandler? Closing;
  public event WindowClosedEventHandler? Closed;
  public event WindowMovedEventHandler? Moved;
  public event WindowTitleEventHandler? TitleChanged;

  private volatile bool AtomicShouldClose;
  public bool ShouldClose => AtomicShouldClose;
  public bool UpdateClient()
  {
    if (!IsOpen)
    {
      return false;
    }
    if (AtomicShouldClose)
    {
      Dispose();
      return false;
    }

    Updated?.Invoke();
    return true;
  }

  public unsafe void Close()
  {
    if (!IsOpen)
    {
      return;
    }

    Glfw.SetWindowShouldClose(HandlePtr, true);
    OnClosing(HandlePtr);
  }

  public unsafe void Dispose()
  {
    if (!IsOpen)
    {
      return;
    }

    var handle = (Lib.WindowHandle*)Interlocked.Exchange(ref AtomicHandlePtr, 0);
    Glfw.DestroyWindow(handle);

    AtomicIsVisible = false;
    AtomicTitle = string.Empty;
    AtomicIsFocused = false;
    AtomicPosition = default;
    AtomicSize = default;

    Closed?.Invoke();
    Event?.Invoke(Backend.ClosedEvent);
    Backend.RemoveWindow(this);
  }

  public unsafe void Hide()
  {
    if (!IsOpen)
    {
      return;
    }

    Glfw.HideWindow(HandlePtr);
    OnVisibility(false);
  }

  public unsafe void Show()
  {
    if (!IsOpen)
    {
      return;
    }

    Glfw.ShowWindow(HandlePtr);
    OnVisibility(true);
  }

  public unsafe void Focus()
  {
    if (!IsOpen)
    {
      return;
    }

    Glfw.FocusWindow(HandlePtr);
  }

  public void PostEvent(WindowEventArgs args)
  {
    if (!IsOpen)
    {
      return;
    }
    Glfw.PostEmptyEvent();

    Event?.Invoke(args);
  }



  private void OnVisibility(bool isVisible)
  {
    Interlocked.Exchange(ref AtomicIsVisible, isVisible);

    VisibilityChanged?.Invoke(isVisible);

    var e = Event;
    if (e != null)
    {
      Backend.VisibilityEvent.IsVisible = isVisible;
      e(Backend.VisibilityEvent);
    }
  }


  private unsafe void OnMinimize(Lib.WindowHandle* handle, bool minimized)
  {
    Interlocked.Exchange(ref AtomicIsVisible, !minimized);

    VisibilityChanged?.Invoke(!minimized);

    var e = Event;
    if (e != null)
    {
      Backend.VisibilityUserEvent.IsVisible = !minimized;
      e(Backend.VisibilityUserEvent);
    }
  }

  private unsafe void OnMaximize(Lib.WindowHandle* handle, bool maximized)
  {
  }

  private unsafe void OnRefresh(Lib.WindowHandle* handle)
  {
    Glfw.PostEmptyEvent();

    Event?.Invoke(Backend.RedrawEvent);
  }


  private unsafe void OnClosing(Lib.WindowHandle* handle)
  {
    bool close = true;

    Closing?.Invoke(out close);

    var e = Event;
    if (e != null)
    {
      Backend.ClosingUserEvent.Close = close;
      e(Backend.ClosingUserEvent);
      close = Backend.ClosingUserEvent.Close;
    }

    if (!close)
    {
      Glfw.SetWindowShouldClose(handle, false);
      return;
    }
    Interlocked.Exchange(ref AtomicShouldClose, true);
    Glfw.PostEmptyEvent();
  }

  private unsafe void OnFocused(Lib.WindowHandle* handle, bool isFocused)
  {
    Interlocked.Exchange(ref AtomicIsFocused, isFocused);

    FocusChanged?.Invoke(isFocused);

    var e = Event;
    if (e != null)
    {
      Backend.FocusedEvent.IsFocused = isFocused;
      e(Backend.FocusedEvent);
    }
  }

  private unsafe void OnMoved(Lib.WindowHandle* handle, int x, int y)
  {
    SetAtomicPosition((x, y));

    Moved?.Invoke(x, y);

    var e = Event;
    if (e != null)
    {
      Backend.MovedEvent.Position = (x, y);
      e(Backend.MovedEvent);
    }
  }

  private unsafe void OnResized(Lib.WindowHandle* handle, int width, int height)
  {
    if (width <= 0 && height <= 0)
    {
      return;
    }
    SetAtomicSize((width, height));

    Resized?.Invoke(width, height);

    var e = Event;
    if (e != null)
    {
      Backend.ResizedEvent.Size = (width, height);
      e(Backend.ResizedEvent);
    }
  }
}