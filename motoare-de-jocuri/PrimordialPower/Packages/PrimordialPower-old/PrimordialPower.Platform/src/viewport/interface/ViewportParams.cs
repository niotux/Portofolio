using System;

namespace PrimordialPower.Platform;

public struct ViewportParams
{
  public ViewportParams() { }



  public ViewportFlags Flags { get; set; } = DefaultFlags;
  public string Title { get; set; } = DefaultTitle;
  public uint Width { get; set; } = DefaultWidth;
  public uint Height { get; set; } = DefaultHeight;
  public int X { get; set; } = DefaultX;
  public int Y { get; set; } = DefaultY;
  public float MinDepth { get; set; } = DefaultMinDepth;
  public float MaxDepth { get; set; } = DefaultMaxDepth;



  public const ViewportFlags DefaultFlags = ViewportFlags.WithShutdownEvents;
  public const string DefaultTitle = "Untitled";
  public const uint DefaultWidth = 800;
  public const uint DefaultHeight = 600;
  public const int DefaultX = 400;
  public const int DefaultY = 300;
  public const float DefaultMinDepth = 0.1f;
  public const float DefaultMaxDepth = 1000f;
  public static readonly ViewportParams Default = new();
  public static readonly ViewportParams Empty = new() { Flags = ViewportFlags.Standard };
}
