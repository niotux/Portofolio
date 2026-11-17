using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using UnivKit.Application;

namespace UnivKit.OS.Windowing;

internal abstract class WindowingNativeServiceBase : IWindowManager, IService
{
  public SurfaceBackend Backend => SurfaceBackend.None;
  public Task Execute(CancellationToken cancellationToken)
  {
    InitCore();
    cancellationToken.Register(WakeUpEventLoopCore);
    WakeUpEventLoopCore();
    while (!cancellationToken.IsCancellationRequested)
    {
      PollEventsCore();
    }
    CloseAll();
    return Task.CompletedTask;
  }



  public int CreatedCount { get; private set; }
  public int OpenCount => this.openWindows.Count;

  public event WindowEventHandler? OnEvent;

  protected readonly object lockOpenCloseWindows = new();
  protected readonly Dictionary<ulong, Window> openWindows = new();
  public IWindow CreateWindow(string title, uint width, uint height, int? x = null, int? y = null)
  {
    return new Window(this, title, width, height, x ?? 0, y ?? 0);
  }

  public void CloseAll()
  {
    foreach (var kvp in this.openWindows)
    {
      CloseCore(kvp.Value);
      kvp.Value.InvalidateInternal();
    }
    lock (this.lockOpenCloseWindows)
    {
      this.openWindows.Clear();
    }
  }

  protected abstract void InitCore();
  protected abstract void PollEventsCore();
  protected abstract void WakeUpEventLoopCore();
  protected abstract ulong GetKeyFromHandleCore(in SurfaceHandle handle);

  protected abstract void OpenCore(Window window);
  protected abstract void CloseCore(Window window);

  internal abstract void SetFocusCore(Window window);
  internal abstract void SetSizeCore(Window window, uint width, uint height);
  internal abstract void SetPositionCore(Window window, int x, int y);
  internal abstract void SetTitleCore(Window window, string text);
  internal abstract void SetStateCore(Window window, WindowState state);

  private readonly ConcurrentQueue<OpenWindowRequest> windowsToOpen = new();

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  internal void Open(Window window) => OpenAsync(window).GetAwaiter().GetResult();

  internal Task OpenAsync(Window window)
  {
    OpenWindowRequest request = new()
    {
      Window = window,
    };
    this.windowsToOpen.Enqueue(request);
    WakeUpEventLoopCore();
    return request.WaitForCompletion();
  }

  internal void TryClose(Window window)
  {
    this.eventArgs.EventType = WindowEventType.ShouldClose;
    SendEvent(window, false);
  }

  internal void Close(Window window)
  {
    CloseCore(window);
    window.InvalidateInternal();
    lock (this.lockOpenCloseWindows)
    {
      this.openWindows.Remove(GetKeyFromHandleCore(window.Handle ?? default));
    }
  }

  internal void FireCloseConfirmed(Window window, bool isServerEvent)
  {
    this.eventArgs.EventType = WindowEventType.CloseConfirmed;
    SendEvent(window, isServerEvent);
  }

  protected WindowEventResponse eventResponse;
  protected WindowEventArgs eventArgs;
  protected void SendEvent(Window window, bool isServerEvent)
  {
    window.HandleEvents(in this.eventArgs, ref this.eventResponse, isServerEvent);
    this.OnEvent?.Invoke(window, in this.eventArgs, ref this.eventResponse);
    this.eventResponse.Reset();
  }


  protected void OpenWindowsFromQueue()
  {
    lock (this.lockOpenCloseWindows)
    {
      while (this.windowsToOpen.TryDequeue(out var request))
      {
        OpenCore(request.Window);
        request.SignalCompletion();
      }
    }
  }


  protected readonly struct OpenWindowRequest
  {
    private readonly TaskCompletionSource tcs;
    private readonly Window window;

    public required Window Window
    {
      get => this.window;
      init
      {
        this.window = value;
        this.tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
      }
    }
    public void SignalCompletion()
    {
      this.tcs.TrySetResult();
    }

    public Task WaitForCompletion()
    {
      return this.tcs.Task;
    }
  }
}
