namespace QuantiX.Engine.Rendering_IGNORE;

/// <summary>
/// Describes one vertex attribute — how the GPU reads a field out of a vertex buffer.
/// </summary>
/// <param name="Location">Shader attribute location (layout(location = N)).</param>
/// <param name="Type">Scalar base type of each component.</param>
/// <param name="Count">Number of components (1–4).</param>
/// <param name="Offset">Byte offset of this attribute within one vertex.</param>
/// <param name="Normalized">
/// Whether integer values are normalized to [0,1] or [-1,1] when read by the shader.
/// </param>
public readonly record struct VertexAttribute(
  uint Location,
  AttributeType Type,
  uint Count,
  uint Offset,
  bool Normalized = false
);
