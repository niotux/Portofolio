namespace QuantiX.Engine.RenderingTMP;

/// <summary>
/// Full vertex layout for a module — attribute list + total stride in bytes.
/// Defined per-module, per-shader. No global or engine-imposed conventions.
/// </summary>
public sealed class VertexLayout
{
  public required VertexAttribute[] Attributes { get; init; }

  /// <summary>Total size of one vertex in bytes.</summary>
  public required int Stride { get; init; }
}
