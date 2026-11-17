namespace QuantiX.Engine.Rendering;

public readonly struct VertexAttribute
{
  public required uint Location { get; init; }
  public required uint Size { get; init; }
  public required uint Stride { get; init; }
  public required uint Offset { get; init; }
}