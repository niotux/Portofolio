namespace QuantiX.Engine.RenderingTMP;

/// <summary>
/// Describes a single vertex attribute as seen by the shader.
/// All layout is defined per-module, per-shader — no global convention.
/// </summary>
public readonly struct VertexAttribute
{
  /// <summary>Shader attribute location (layout location = N).</summary>
  public required int Location { get; init; }

  /// <summary>Underlying scalar type for each component.</summary>
  public required VertexAttributeBaseType BaseType { get; init; }

  /// <summary>Number of components: 1, 2, 3, or 4.</summary>
  public required int ComponentCount { get; init; }

  /// <summary>Byte offset of this attribute within a single vertex.</summary>
  public required int Offset { get; init; }

  /// <summary>
  /// When true, integer types are normalized to [0,1] or [-1,1] before the shader sees them.
  /// Has no effect on Float or Double base types.
  /// </summary>
  public bool Normalized { get; init; }
}
