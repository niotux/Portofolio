using System.Collections.Concurrent;
using PrimordialPower.Runtime;

namespace PrimordialPower.Platform;

public abstract class ViewportManagerServerBase : AppServer, IViewportManager<Viewport>
{
  // Viewport Manager Main Runtime

  protected readonly HashSet<Viewport> viewports = new();
  protected readonly ConcurrentQueue<(Viewport Viewport, TaskCompletionSource TCS)> viewportsToOpen = new();
  protected readonly object lockOpeningViewports = new();



  public override bool Loop()
  {
    PollEvents();
    return !this.StopRequested;
  }

  public override void OnStop()
  {
    WakeUpEventLoop();
  }

  public override void Shutdown()
  {
    CloseAll();
  }



  protected abstract void PollEvents();
  protected abstract void OpenImpl(Viewport viewport);
  protected abstract void WakeUpEventLoop();



  protected void OpenWindowsFromQueue()
  {
    lock (this.lockOpeningViewports)
    {
      while (this.viewportsToOpen.TryDequeue(out var openRequest))
      {
        try
        {
          OpenImpl(openRequest.Viewport);
        }
        finally
        {
          openRequest.TCS.SetResult();
        }
      }
    }
  }

  // Viewport Manager Services

  public int BoundViewportCount => this.viewports.Count;
  public abstract int OpenViewportCount { get; }



  public abstract event Action? OnAllViewportsClosed;
  public abstract event Action<ViewportEvent>? OnPollEvents;



  public abstract void Destroy(Viewport viewport);
  public abstract void SetFocus(Viewport viewport, bool isFocused);
  public abstract void SetSize(Viewport viewport, uint width, uint height);
  public abstract void SetPosition(Viewport viewport, int x, int y);
  public abstract void SetSizePosition(Viewport viewport, uint width, uint height, int x, int y);
  public abstract void SetTitle(Viewport viewport, string text);
  public abstract void CloseAll();



  public void Bind(Viewport viewport)
  {
    if (viewport.Manager == this) return;

    viewport.Manager?.Unbind(viewport);
    this.viewports.Add(viewport);
    viewport.AsInternal.ChangeInternalManger(this);
  }

  public void Unbind(Viewport viewport)
  {
    if (viewport.Manager != this) return;

    this.Destroy(viewport);
    this.viewports.Remove(viewport);
    viewport.AsInternal.ChangeInternalManger(null);
  }

  public void Open(Viewport viewport)
  {
    OpenAsync(viewport).GetAwaiter().GetResult();
  }

  public Task OpenAsync(Viewport viewport)
  {
    if (viewport.IsOpen) return Task.CompletedTask;
    Bind(viewport);
    TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    this.viewportsToOpen.Enqueue((viewport, tcs));
    WakeUpEventLoop();
    return tcs.Task;
  }

  public void Close(Viewport viewport)
  {
    viewport.Close();
  }

  public void OpenAll()
  {
    foreach (var viewport in this.viewports)
    {
      Open(viewport);
    }
  }

  public bool Update(Viewport viewport)
  {
    if (viewport.AsInternal.ConfirmDestroy())
    {
      Destroy(viewport);
      return false;
    }
    return true;
  }
}