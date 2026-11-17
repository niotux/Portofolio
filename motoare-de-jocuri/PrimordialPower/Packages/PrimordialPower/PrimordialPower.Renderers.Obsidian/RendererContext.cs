using PrimordialPower.Platform;

namespace PrimordialPower.Renderers.Obsidian;

public class RendererContext : IDisposable
{
  private readonly Renderer renderer;
  private readonly IViewport viewport;
  private IntPtr handle;

  private volatile bool resizePending;
  private uint pendingWidth;
  private uint pendingHeight;

  internal RendererContext(Renderer renderer, IViewport viewport, IntPtr nativeHandle)
  {
    this.renderer = renderer;
    this.viewport = viewport;
    this.handle = nativeHandle;

    this.viewport.OnSizeUpdate += OnViewportResize;
  }

  public void DrawFrame()
  {
    if (handle == 0) return;

    if (resizePending)
    {
      resizePending = false;
      Renderer.RendererContext_RecreateSwapchain(handle, pendingWidth, pendingHeight);
    }

    Renderer.RendererContext_DrawFrame(handle);
  }

  private void OnViewportResize(uint width, uint height)
  {
    pendingWidth = width;
    pendingHeight = height;
    resizePending = true;
  }

  ~RendererContext()
  {
    DisposeImpl();
  }

  public void Dispose()
  {
    DisposeImpl();
    GC.SuppressFinalize(this);
  }

  private void DisposeImpl()
  {
    viewport.OnSizeUpdate -= OnViewportResize;

    IntPtr inst = Interlocked.Exchange(ref handle, 0);
    if (inst == 0) return;
    Renderer.RendererContext_Dispose(inst);
  }
}
