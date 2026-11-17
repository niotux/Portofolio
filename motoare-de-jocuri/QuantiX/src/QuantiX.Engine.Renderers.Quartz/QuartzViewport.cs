using GL = Silk.NET.OpenGL;
using QuantiX.Engine.Rendering;
using QuantiX.Engine.Windowing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace QuantiX.Engine.Renderers.Quartz;

internal sealed class QuartzViewport : IViewport
{
  private readonly IWindow _window;
  internal readonly GL.GL Gl;
  internal readonly nint GlContext;
  internal readonly nint DeviceContext;
  internal uint Vao;

  private readonly nint _cachedHWnd;
  private readonly nint _cachedX11Display;
  private readonly ulong _cachedX11Window;
  private bool _disposed;

  private uint _framebufferWidth;
  private uint _framebufferHeight;

  public QuartzViewport(IWindow window)
  {
    _window = window;
    var handle = window.Handle;

    var (w, h) = window.Size;
    _framebufferWidth = w;
    _framebufferHeight = h;

    if (WindowingService.IsWin32)
    {
      _cachedHWnd = handle.Win32.HWnd;
      DeviceContext = Windows.WGLApi.GetDC(_cachedHWnd);

      var pfd = new Windows.WGLApi.PixelFormatDescriptor
      {
        nSize = (ushort)System.Runtime.InteropServices.Marshal.SizeOf<Windows.WGLApi.PixelFormatDescriptor>(),
        nVersion = 1,
        dwFlags = Windows.WGLApi.PixelFormatDescriptor.PFD_DRAW_TO_WINDOW
                | Windows.WGLApi.PixelFormatDescriptor.PFD_SUPPORT_OPENGL
                | Windows.WGLApi.PixelFormatDescriptor.PFD_DOUBLEBUFFER,
        iPixelType = Windows.WGLApi.PixelFormatDescriptor.PFD_TYPE_RGBA,
        cColorBits = 32,
        cDepthBits = 24,
        cStencilBits = 8,
        iLayerType = Windows.WGLApi.PixelFormatDescriptor.PFD_MAIN_PLANE
      };

      int pixelFormat = Windows.WGLApi.ChoosePixelFormat(DeviceContext, ref pfd);
      Windows.WGLApi.SetPixelFormat(DeviceContext, pixelFormat, ref pfd);

      GlContext = Windows.WGLApi.CreateContext(DeviceContext);
      Windows.WGLApi.MakeCurrent(DeviceContext, GlContext);

      Gl = GL.GL.GetApi(Windows.WGLApi.GetProcAddress);

      DisableVSyncWin32();
    }
    else if (WindowingService.IsX11)
    {
      var display = _cachedX11Display = handle.X11.Display;
      var x11Window = _cachedX11Window = handle.X11.Window;

      int result = Linux.GLXApi.XGetWindowAttributes(display, x11Window, out var xwa);
      if (result == 0)
      {
        throw new Exception("XGetWindowAttributes failed");
      }
      if (xwa.visual == nint.Zero)
      {
        throw new Exception("XWindowAttributes.visual is null");
      }

      var visualid = Marshal.ReadIntPtr(xwa.visual, IntPtr.Size);
      if (visualid == nint.Zero)
      {
        throw new Exception("visualid read from Visual* is zero");
      }

      var tmpl = new Linux.GLXApi.XVisualInfo
      {
        visualid = (nuint)Marshal.ReadIntPtr(xwa.visual, IntPtr.Size)
      };

      var viPtr = Linux.GLXApi.XGetVisualInfo(display, Linux.GLXApi.VisualIDMask, ref tmpl, out int count);
      if (viPtr == nint.Zero || count == 0)
      {
        throw new Exception("XGetVisualInfo returned no results");
      }

      GlContext = Linux.GLXApi.CreateContext(display, viPtr, nint.Zero, true);
      if (GlContext == nint.Zero)
      {
        throw new Exception("glXCreateContext failed");
      }

      Linux.GLXApi.MakeCurrent(display, x11Window, GlContext);
      DeviceContext = display;

      Gl = GL.GL.GetApi(Linux.GLXApi.GetProcAddress);

      DisableVSyncX11(display, x11Window);
    }
    else
    {
      throw new PlatformNotSupportedException("Unsupported windowing backend.");
    }

    Vao = Gl.GenVertexArray();

    _window.OnSizeUpdate += (w, h) =>
    {
      _framebufferWidth = w;
      _framebufferHeight = h;
    };
  }

  public GPUObjectHandle ApiHandle => new() { ApiHandleValue = 0 };
  public ISurface InnerSurface => _window;

  public void Draw(Pipeline[] pipelines)
  {
    MakeCurrent();
    QuartzPipelineDrawer.Draw(Gl, Vao, 0, _framebufferWidth, _framebufferHeight, pipelines);
  }

  public void Present()
  {
    SwapBuffers();
  }

  [SupportedOSPlatform("windows")]
  private static unsafe void DisableVSyncWin32()
  {
    nint addr = Windows.WGLApi.GetProcAddress("wglSwapIntervalEXT");
    if (addr != 0)
      ((delegate* unmanaged<int, int>)addr)(0);
  }

  [SupportedOSPlatform("linux")]
  private static unsafe void DisableVSyncX11(nint display, ulong drawable)
  {
    nint extAddr = Linux.GLXApi.GetProcAddress("glXSwapIntervalEXT");
    if (extAddr != 0)
    {
      ((delegate* unmanaged<nint, ulong, int, void>)extAddr)(display, drawable, 0);
      return;
    }
    nint mesaAddr = Linux.GLXApi.GetProcAddress("glXSwapIntervalMESA");
    if (mesaAddr != 0)
      ((delegate* unmanaged<int, int>)mesaAddr)(0);
  }

  internal void MakeCurrent()
  {
    if (WindowingService.IsWin32)
    {
      Windows.WGLApi.MakeCurrent(DeviceContext, GlContext);
    }
    else if (WindowingService.IsX11)
    {
      Linux.GLXApi.MakeCurrent(_cachedX11Display, _cachedX11Window, GlContext);
    }
  }

  internal void SwapBuffers()
  {
    if (WindowingService.IsWin32)
    {
      Windows.WGLApi.SwapBuffers(DeviceContext);
    }
    else if (WindowingService.IsX11)
    {
      Linux.GLXApi.SwapBuffers(_cachedX11Display, _cachedX11Window);
    }
  }

  // ISurface — framebuffer size (may differ from window size on HiDPI)
  public (uint Width, uint Height) Size
  {
    get => (_framebufferWidth, _framebufferHeight);
    set => _window.Size = value;
  }

  public (int X, int Y) Position
  {
    get => _window.Position;
    set => _window.Position = value;
  }

  public bool IsOpen => _window.IsOpen;
  public bool IsVisible => _window.IsVisible;

  public event Action<uint, uint>? OnSizeUpdate
  {
    add => _window.OnSizeUpdate += value;
    remove => _window.OnSizeUpdate -= value;
  }

  public event Action<int, int>? OnPositionUpdate
  {
    add => _window.OnPositionUpdate += value;
    remove => _window.OnPositionUpdate -= value;
  }

  public event Action<bool>? OnVisibilityUpdate
  {
    add => _window.OnVisibilityUpdate += value;
    remove => _window.OnVisibilityUpdate -= value;
  }

  public event Action? OnClose
  {
    add => _window.OnClose += value;
    remove => _window.OnClose -= value;
  }

  public event Action? OnDestroy
  {
    add => _window.OnDestroy += value;
    remove => _window.OnDestroy -= value;
  }

  public void SetSizePosition(uint width, uint height, int x, int y)
    => _window.SetSizePosition(width, height, x, y);

  public void Open() => _window.Open();
  public Task OpenAsync() => _window.OpenAsync();
  public void Close() => _window.Close();
  public bool Update() => _window.Update();

  public void Dispose()
  {
    if (_disposed) return;
    _disposed = true;

    Gl.DeleteVertexArray(Vao);

    if (WindowingService.IsWin32)
    {
      Windows.WGLApi.MakeCurrent(DeviceContext, nint.Zero);
      Windows.WGLApi.DeleteContext(GlContext);
      Windows.WGLApi.ReleaseDC(_cachedHWnd, DeviceContext);
    }
    else if (WindowingService.IsX11)
    {
      Linux.GLXApi.MakeCurrent(_cachedX11Display, 0, nint.Zero);
      Linux.GLXApi.DestroyContext(_cachedX11Display, GlContext);
    }

    Gl.Dispose();
  }
}
