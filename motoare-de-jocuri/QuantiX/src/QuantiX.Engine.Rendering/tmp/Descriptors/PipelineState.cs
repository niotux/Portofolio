namespace QuantiX.Engine.RenderingTMP;

public readonly struct PipelineState
{
  public BlendState Blend { get; init; }
  public DepthState Depth { get; init; }
  public RasterizerState Rasterizer { get; init; }
  public PrimitiveTopology Topology { get; init; }

  /// <summary>Standard opaque 3D geometry.</summary>
  public static readonly PipelineState Default = new()
  {
    Blend = BlendState.Disabled,
    Depth = DepthState.Default,
    Rasterizer = RasterizerState.Default,
    Topology = PrimitiveTopology.Triangles,
  };

  /// <summary>Alpha-blended transparent geometry — depth test but no depth write.</summary>
  public static readonly PipelineState Transparent = new()
  {
    Blend = BlendState.AlphaBlend,
    Depth = DepthState.ReadOnly,
    Rasterizer = RasterizerState.NoCull,
    Topology = PrimitiveTopology.Triangles,
  };

  /// <summary>Additive blending, no depth testing — suitable for particles and glows.</summary>
  public static readonly PipelineState Additive = new()
  {
    Blend = BlendState.Additive,
    Depth = DepthState.Disabled,
    Rasterizer = RasterizerState.NoCull,
    Topology = PrimitiveTopology.Triangles,
  };

  /// <summary>Wireframe — useful for debug overlays.</summary>
  public static readonly PipelineState Wireframe = new()
  {
    Blend = BlendState.Disabled,
    Depth = DepthState.Default,
    Rasterizer = RasterizerState.Wireframe,
    Topology = PrimitiveTopology.Triangles,
  };
}
