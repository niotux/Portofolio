using System.Diagnostics.CodeAnalysis;

namespace QuantiX.Engine.Rendering;

public readonly struct Pipeline
{
  [SetsRequiredMembers]
  public Pipeline(IShader shader, PipelineState state, VertexAttribute[] attributes, DrawEntry[] drawEntries)
  {
    this.ShaderApiHandle = shader.ApiHandle;
    this.DrawEntries = drawEntries;
    this.VertexAttributes = attributes;
    this.State = state;
  }

  public required PipelineState State { get; init; }
  public required GPUObjectHandle ShaderApiHandle { get; init; }
  public required VertexAttribute[] VertexAttributes { get; init; }
  public required DrawEntry[] DrawEntries { get; init; }
}