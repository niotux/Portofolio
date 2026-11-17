namespace QuantiX.Engine.RenderingTMP;

public sealed class RenderableModuleDescriptor
{
  /// <summary>Compiled shader that defines the vertex attributes and uniforms for this module.</summary>
  public required IShader Shader { get; init; }

  /// <summary>Vertex layout matching the shader's input attributes.</summary>
  public required VertexLayout VertexLayout { get; init; }

  /// <summary>Pipeline state applied once per frame before flushing this module's draws.</summary>
  public PipelineState PipelineState { get; init; } = PipelineState.Default;

  /// <summary>Initial static pool capacity in vertices. Throws if exceeded (future: auto-resize).</summary>
  public int StaticVertexCapacity { get; init; } = 65536;

  /// <summary>Initial static pool capacity in indices.</summary>
  public int StaticIndexCapacity { get; init; } = 131072;

  /// <summary>Per-frame dynamic pool capacity in vertices. Reset each BeginFrame.</summary>
  public int DynamicVertexCapacity { get; init; } = 16384;

  /// <summary>Per-frame dynamic pool capacity in indices.</summary>
  public int DynamicIndexCapacity { get; init; } = 32768;

  /// <summary>
  /// Draw order relative to other modules. Lower values are drawn first.
  /// Suggested convention: 0 = opaque, 100 = transparent, 200 = UI/overlay.
  /// </summary>
  public int RenderOrder { get; init; } = 0;
}
