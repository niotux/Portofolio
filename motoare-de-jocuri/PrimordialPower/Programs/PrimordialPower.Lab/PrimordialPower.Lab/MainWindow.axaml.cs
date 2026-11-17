using System;
using System.Data;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace PrimordialPower.Lab;

public partial class MainWindow : Window
{
  private readonly Border worker;
  private readonly Border titleBar;

  public MainWindow()
  {
    AvaloniaXamlLoader.Load(this);
    this.worker = this.FindControl<Border>("WorkerWindow") ?? throw NullControl();
    this.titleBar = this.FindControl<Border>("WorkerWindow_TitleBar") ?? throw NullControl();

    this.titleBar.PointerPressed += HandleDragOnPressed;
    this.titleBar.PointerMoved += HandleDragOnMove;
    this.titleBar.PointerReleased += HandleDragOnReleased;
  }


  private Exception NullControl()
  {
    return new NoNullAllowedException("Could now find control");
  }



  private Point dragStart;
  private bool isDragging;
  private void HandleDragOnPressed(object? s, PointerPressedEventArgs e)
  {
    this.isDragging = true;
    this.dragStart = e.GetPosition(this);
    e.Pointer.Capture(this.titleBar);
  }
  private void HandleDragOnReleased(object? s, PointerReleasedEventArgs e)
  {
    this.isDragging = false;
    e.Pointer.Capture(null);
  }
  private void HandleDragOnMove(object? s, PointerEventArgs e)
  {
    if (this.isDragging)
    {
      Point curr = e.GetPosition(this);
      Canvas.SetLeft(this.worker, curr.X - this.dragStart.X);
      Canvas.SetTop(this.worker, curr.Y - this.dragStart.Y);
    }
  }
}