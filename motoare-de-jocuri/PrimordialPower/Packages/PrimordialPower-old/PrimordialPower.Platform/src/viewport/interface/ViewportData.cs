using System;
using System.Runtime.InteropServices;

namespace PrimordialPower.Platform;


[StructLayout(LayoutKind.Sequential)]
public struct ViewportData
{
  public ViewportData() { }
  public uint Width = 0;
  public uint Height = 0;
  public int XOffset = 0;
  public int YOffset = 0;
  public float MinDepth = 0f;
  public float MaxDepth = 0f;
  public ViewportNative Native = new();
}
