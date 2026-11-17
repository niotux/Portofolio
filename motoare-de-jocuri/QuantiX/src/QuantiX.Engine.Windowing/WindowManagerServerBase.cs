using System.Collections.Concurrent;
using QuantiX.Engine;

namespace QuantiX.Engine.Windowing;

public abstract class WindowManagerServerBase : AppServer, IWindowManager<Window>
{
  // Window Manager Main Runtime

  protected readonly HashSet<Window> windows = new();
  protected readonly ConcurrentQueue<(Window Window, TaskCompletionSource TCS)> windowsToOpen = new();
  protected readonly object lockOpening = new();



  protected sealed override void OnLoop()
  {
    PollEvents();
  }

  protected sealed override void OnStop()
  {
    WakeUpEventLoop();
  }

  protected sealed override void OnShutdown()
  {
    CloseAll();
  }



  protected abstract void PollEvents();
  protected abstract void OpenImpl(Window window);
  protected abstract void WakeUpEventLoop();



  protected void OpenWindowsFromQueue()
  {
    lock (this.lockOpening)
    {
      while (this.windowsToOpen.TryDequeue(out var openRequest))
      {
        try
        {
          OpenImpl(openRequest.Window);
        }
        finally
        {
          openRequest.TCS.SetResult();
        }
      }
    }
  }

  // Window Manager Services

  public int BoundCount => this.windows.Count;
  public abstract int OpenCount { get; }



  public abstract event Action? OnAllClosed;
  public abstract event Action<WindowEvent>? OnPollEvents;



  public abstract void Destroy(Window window);
  public abstract void SetFocus(Window window, bool isFocused);
  public abstract void SetSize(Window window, uint width, uint height);
  public abstract void SetPosition(Window window, int x, int y);
  public abstract void SetSizePosition(Window window, uint width, uint height, int x, int y);
  public abstract void SetTitle(Window window, string text);
  public abstract void CloseAll();



  public Window CreateWindow(string title, uint width, uint height, int? x = null, int? y = null)
  {
    var win = new Window(title, width, height, x, y);
    Bind(win);
    return win;
  }

  public void Bind(Window window)
  {
    if (window.Manager == this) return;

    window.Manager?.Unbind(window);
    this.windows.Add(window);
    window.AsInternal.ChangeInternalManger(this);
  }

  public void Unbind(Window window)
  {
    if (window.Manager != this) return;

    this.Destroy(window);
    this.windows.Remove(window);
    window.AsInternal.ChangeInternalManger(null);
  }

  public void Open(Window window)
  {
    OpenAsync(window).GetAwaiter().GetResult();
  }

  public Task OpenAsync(Window window)
  {
    if (window.IsOpen) return Task.CompletedTask;
    Bind(window);
    var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    this.windowsToOpen.Enqueue((window, tcs));
    WakeUpEventLoop();
    return tcs.Task;
  }

  public void Close(Window window)
  {
    window.Close();
  }

  public void OpenAll()
  {
    foreach (var window in this.windows)
    {
      Open(window);
    }
  }

  public bool Update(Window window)
  {
    if (window.AsInternal.ConfirmDestroy())
    {
      Destroy(window);
      return false;
    }
    return true;
  }
}