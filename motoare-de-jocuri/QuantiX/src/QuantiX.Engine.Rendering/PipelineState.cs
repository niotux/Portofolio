namespace QuantiX.Engine.Rendering;

public readonly struct PipelineState
{
  public required CullMode CullMode { get; init; }
  public required FillMode FillMode { get; init; }
  public required FrontFace FrontFace { get; init; }
  public required DepthFunc DepthFunc { get; init; }
  public required bool DepthWrite { get; init; }
  public required BlendFactor SrcBlendFactor { get; init; }
  public required BlendFactor DstBlendFactor { get; init; }
  public required PrimitiveTopology Topology { get; init; }
  public float DepthBiasConstant { get; init; }
  public float DepthBiasSlope { get; init; }
}
