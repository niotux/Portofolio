namespace QuantiX.Engine.Rendering_IGNORE;

/// <summary>
/// Full static description of a rendering pipeline.
/// Passed to IRenderer.CreatePipeline — all fields fixed at creation time.
/// </summary>
public sealed class PipelineDesc
{
  public required IShader Shader { get; init; }
  public required VertexLayout Layout { get; init; }

  public BlendMode BlendMode { get; init; } = BlendMode.Opaque;
  public bool DepthTest { get; init; } = true;
  public bool DepthWrite { get; init; } = true;
  public CullMode CullMode { get; init; } = CullMode.Back;
  public bool Wireframe { get; init; } = false;

  /// <summary>
  /// Controls render ordering. Lower values are drawn first.
  /// Convention: 0 = opaque, 100 = transparent, 200 = UI.
  /// </summary>
  public int DrawOrder { get; init; } = 0;
}
