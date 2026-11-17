using System;
using System.Runtime.InteropServices;

namespace UnivKit.OS.Windowing;

public delegate void WindowEventHandler(IWindow window, in WindowEventArgs args, ref WindowEventResponse response);

public struct WindowEventArgs
{
  public WindowEventType EventType { get; set; }


  private SizeChangedWindowEvent newSize;
  public SizeChangedWindowEvent SizeChangedArgs
  {
    set
    {
      this.newSize = value;
      this.EventType = WindowEventType.SizeChanged;
    }
    get => this.EventType is WindowEventType.SizeChanged
      ? this.newSize
      : throw GetArgException(nameof(SizeChangedArgs));
  }


  private VisibilityChangedWindowEvent visibilityChanged;
  public VisibilityChangedWindowEvent VisibilityChangedArgs
  {
    set
    {
      this.visibilityChanged = value;
      this.EventType = WindowEventType.Visibile;
    }
    get => this.EventType is WindowEventType.Visibile or WindowEventType.Hidden
      ? this.visibilityChanged
      : throw GetArgException(nameof(VisibilityChangedArgs));
  }


  private PositionChangedWindowEvent newPosition;
  public PositionChangedWindowEvent PositionChangedArgs
  {
    set
    {
      this.newPosition = value;
      this.EventType = WindowEventType.PositionChanged;
    }
    get => this.EventType == WindowEventType.PositionChanged
      ? this.newPosition
      : throw GetArgException(nameof(PositionChangedArgs));
  }


  private TitleChangedWindowEvent newTitle;
  public TitleChangedWindowEvent TitleChangedArgs
  {
    set
    {
      this.newTitle = value;
      this.EventType = WindowEventType.TitleChanged;
    }
    get => this.EventType == WindowEventType.TitleChanged
      ? this.newTitle
      : throw GetArgException(nameof(TitleChangedArgs));
  }


  private CursorMovedWindowEvent cursorMoved;
  public CursorMovedWindowEvent CursorMovedArgs
  {
    set
    {
      this.cursorMoved = value;
      this.EventType = WindowEventType.CursorMoved;
    }
    get => this.EventType == WindowEventType.CursorMoved
      ? this.cursorMoved
      : throw GetArgException(nameof(CursorMovedArgs));
  }


  private CursorActionWindowEvent cursorAction;
  public CursorActionWindowEvent CursorActionArgs
  {
    set
    {
      this.cursorAction = value;
      this.EventType = WindowEventType.CursorAction;
    }
    get => this.EventType == WindowEventType.CursorAction
      ? this.cursorAction
      : throw GetArgException(nameof(CursorActionArgs));
  }


  private KeyPressedWindowEvent keyPressed;
  public KeyPressedWindowEvent KeyPressedArgs
  {
    set
    {
      this.keyPressed = value;
      this.EventType = WindowEventType.KeyPressed;
    }
    get => this.EventType == WindowEventType.KeyPressed
      ? this.keyPressed
      : throw GetArgException(nameof(KeyPressedArgs));
  }


  private ArgumentException GetArgException(string argName)
  {
    return new ArgumentException($"Window event argument [{argName}] is not valid for the event type of [{this.EventType}]!", argName);
  }
}

public enum WindowEventType
{
  Unknown,
  SizeChanged,
  PositionChanged,
  TitleChanged,
  CursorMoved,
  CursorAction,
  KeyPressed,
  Visibile,
  Hidden,
  FocusIn,
  FocusOut,
  ShouldClose,
  CloseConfirmed,
  Closed,
}

public readonly struct SizeChangedWindowEvent
{
  public required uint Width { get; init; }
  public required uint Height { get; init; }
}

public readonly struct VisibilityChangedWindowEvent
{
  public required WindowState State { get; init; }
}

public readonly struct PositionChangedWindowEvent
{
  public required int X { get; init; }
  public required int Y { get; init; }
}

public readonly struct CursorMovedWindowEvent
{
  public required int X { get; init; }
  public required int Y { get; init; }
}

public readonly struct CursorActionWindowEvent
{
  public required int X { get; init; }
  public required int Y { get; init; }
}

public readonly struct KeyPressedWindowEvent
{
  public required int X { get; init; }
  public required int Y { get; init; }
}

public readonly struct TitleChangedWindowEvent
{
  public required string Title { get; init; }
}

public struct WindowEventResponse
{
  public bool TryCancelEvent { get; set; }
  public bool EventWasHandled { get; set; }

  public void Reset()
  {
    this = new();
  }
}