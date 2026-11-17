using System;
using System.Collections.Generic;

namespace Texp.Core;

/// <summary>A box over the file list: help, properties, or a list to pick from.</summary>
public sealed class Overlay
{
  public readonly string Title;
  public readonly List<string> Lines = new();
  /// <summary>When set, lines are choices: Enter or a click calls it with the line's index.</summary>
  public readonly Action<int>? OnPick;
  public int Cursor;
  public int Scroll;

  public Overlay(string title, Action<int>? onPick)
  {
    Title = title;
    OnPick = onPick;
  }

  public bool IsPicker
  {
    get { return OnPick != null; }
  }
}
