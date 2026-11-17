namespace QuantiX.Engine.Rendering_IGNORE;

/// <summary>
/// Describes the memory layout of a single vertex — stride and per-attribute descriptions.
/// </summary>
public sealed class VertexLayout
{
  public VertexLayout(uint stride, params VertexAttribute[] attributes)
  {
    Stride = stride;
    Attributes = attributes;
  }

  /// <summary>Byte size of one complete vertex.</summary>
  public uint Stride { get; }

  /// <summary>All attribute bindings for this vertex format.</summary>
  public IReadOnlyList<VertexAttribute> Attributes { get; }
}
