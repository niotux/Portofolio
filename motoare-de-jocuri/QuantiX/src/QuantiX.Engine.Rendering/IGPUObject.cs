using System;

namespace QuantiX.Engine.Rendering;

public interface IGPUObject : IDisposable
{
  public GPUObjectHandle ApiHandle { get; }
}
