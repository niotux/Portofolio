using System.Diagnostics.CodeAnalysis;

namespace QuantiX.Engine.Rendering;

public struct DrawEntry
{
  [SetsRequiredMembers]
  public DrawEntry(ReadOnlySpan<ITexture> textures, IBuffer vertexBuffer, IBuffer indexBuffer, IBuffer uniformBuffer, int vertexOffset, uint indexStart, uint indexCount, uint uniformStart, uint uniformSize)
  {
    this.BoundTextures = new GPUObjectHandle[textures.Length];
    for (int i = 0; i < textures.Length; i++)
    {
      this.BoundTextures[i] = textures[i].ApiHandle;
    }

    this.VertexBuffer = vertexBuffer.ApiHandle;
    this.IndexBuffer = indexBuffer.ApiHandle;
    this.UniformBuffer = uniformBuffer.ApiHandle;

    this.VertexOffset = vertexOffset;
    this.IndexStart = indexStart;
    this.IndexCount = indexCount;
    this.UniformStart = uniformStart;
    this.UniformSize = uniformSize;
  }

  public required GPUObjectHandle[] BoundTextures { get; set; }
  public required GPUObjectHandle VertexBuffer { get; set; }
  public required GPUObjectHandle IndexBuffer { get; set; }
  public required GPUObjectHandle UniformBuffer { get; set; }
  public required int VertexOffset { get; set; }
  public required uint IndexStart { get; set; }
  public required uint IndexCount { get; set; }
  public required uint UniformStart { get; set; }
  public required uint UniformSize { get; set; }
}
