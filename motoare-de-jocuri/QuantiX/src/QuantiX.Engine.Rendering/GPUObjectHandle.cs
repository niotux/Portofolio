using System;

namespace QuantiX.Engine.Rendering;

public readonly struct GPUObjectHandle
{
  public required ulong ApiHandleValue { get; init; }
}
