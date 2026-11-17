using OpenUL.Windowing.Desktop;
using OpenUL.Windowing.GLFW.Imports;
using OpenUL.Windowing.Platform;
using LibGLFW = Silk.NET.GLFW;

namespace OpenUL.Windowing.GLFW;

public sealed class GLFWWindowingBackend : IWindowingBackend<GLFWWindow, DesktopWindowProps>
{
  private readonly LibGLFW.Glfw Glfw;
  private readonly HashSet<GLFWWindow> OpenWindows = new();
  internal static GLFWWindowingBackend Instance { get; } = new();
  private GLFWWindowingBackend()
  {
    Glfw = LibGLFW.Glfw.GetApi();
    if (OperatingSystem.IsLinux())
    {
      Glfw.InitHint((LibGLFW.InitHint)0x00050003, 0x00060004);
    }
    Glfw.SetErrorCallback(OnError);
    IsSupported = Glfw.Init();
  }

  public event Action<Exception>? Error;
  private void OnError(LibGLFW.ErrorCode err, string description)
  {
    bool ignore = (GLFWErrorCode)err
      is GLFWErrorCode.FeatureUnavailable
      or GLFWErrorCode.FeatureUnimplemented
      or GLFWErrorCode.CursorUnavailable;

    if (ignore)
    {
      return;
    }

    Error?.Invoke(new GLFWException(description, (GLFWErrorCode)err));
  }


  private bool Disposed = false;
  public void Dispose()
  {
    if (Disposed)
    {
      return;
    }

    Disposed = true;
    GC.SuppressFinalize(this);

    DisposeAll();
    Glfw.Dispose();
  }


  public bool IsSupported { get; private set; }
  public int OpenCount => OpenWindows.Count;


  internal unsafe WindowHandle CreateWindowHandle(LibGLFW.WindowHandle* window)
  {
    var natWin = new LibGLFW.GlfwNativeWindow(Glfw, window);
    if (natWin.Win32 is { } win32)
    {
      return Win32WindowHandle.Create(win32.Hwnd, win32.HInstance);
    }
    if (natWin.X11 is { } x11)
    {
      return X11WindowHandle.Create(x11.Display, x11.Window);
    }
    if (natWin.Cocoa is { } cocoa)
    {
      return MacOSWindowHandle.Create(cocoa);
    }
    if (natWin.Wayland is { } wayland)
    {
      return WaylandWindowHandle.Create(wayland.Display, wayland.Surface);
    }
    if (natWin.Android is { } android)
    {
      return AndroidWindowHandle.Create(android.Window);
    }
    return default;
  }

  public GLFWWindow OpenWindow(DesktopWindowProps properties)
  {
    unsafe
    {
      Glfw.WindowHint(LibGLFW.WindowHintClientApi.ClientApi, LibGLFW.ClientApi.NoApi);
      Glfw.WindowHint(LibGLFW.WindowHintContextApi.ContextCreationApi, LibGLFW.ContextApi.NativeContextApi);

      Glfw.WindowHint(LibGLFW.WindowHintBool.TransparentFramebuffer, properties.Transparent);
      Glfw.WindowHint(LibGLFW.WindowHintBool.Decorated, properties.Decorated);
      Glfw.WindowHint(LibGLFW.WindowHintBool.FocusOnShow, properties.FocusOnShow);
      Glfw.WindowHint(LibGLFW.WindowHintBool.Resizable, properties.Resizable);
      Glfw.WindowHint(LibGLFW.WindowHintBool.Focused, properties.Focused);
      Glfw.WindowHint(LibGLFW.WindowHintBool.Visible, properties.Visible);
      var handle = Glfw.CreateWindow(
        properties.Size.Width,
        properties.Size.Height,
        properties.Title ?? string.Empty,
        null,
        null
      );

      if (handle == null)
      {
        Glfw.GetError(out var description);
        int count = 0;
        while (description[count] != 0)
        {
          count++;
        }
        string text = new((sbyte*)description, 0, count);
        throw new LibGLFW.GlfwException(text);
      }

      if (properties.Position is not null)
      {
        var (x, y) = properties.Position.Value;
        Glfw.SetWindowPos(handle, x, y);
      }

      GLFWWindow window = new(this, Glfw, handle, properties);
      OpenWindows.Add(window);
      return window;
    }
  }

  public GLFWWindow OpenWindow()
    => OpenWindow(DesktopWindowProps.DefaultUntitled);

  public void PollEvents()
  {
    Glfw.PollEvents();
  }

  public void WaitEvents()
  {
    Glfw.WaitEvents();
  }

  public void UpdateAllClients()
  {
    foreach (var win in OpenWindows)
    {
      win.UpdateClient();
    }
  }

  public void CloseAll()
  {
    foreach (var win in OpenWindows)
    {
      win.Close();
    }
  }

  public void DisposeAll()
  {
    foreach (var win in OpenWindows)
    {
      win.Dispose();
    }
  }

  internal void RemoveWindow(GLFWWindow window)
  {
    OpenWindows.Remove(window);
  }

  internal Events.WindowClosedEventArgs ClosedEvent { get; } = new();
  internal Events.WindowClosingEventArgs ClosingUserEvent { get; } = new();
  internal Events.WindowFocusEventArgs FocusedEvent { get; } = new();
  internal Events.WindowUpdateEventArgs RedrawEvent { get; } = new();
  internal Events.WindowResizedEventArgs ResizedEvent { get; } = new();
  internal Events.WindowMovedEventArgs MovedEvent { get; } = new();
  internal Events.WindowVisibilityEventArgs VisibilityEvent { get; } = new();
  internal Events.WindowVisibilityEventArgs VisibilityUserEvent { get; } = new();
  internal Events.WindowTitleEventArgs TitleEvent { get; } = new();
}
