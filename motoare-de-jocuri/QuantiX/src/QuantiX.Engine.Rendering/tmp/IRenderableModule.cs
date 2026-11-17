namespace QuantiX.Engine.RenderingTMP;

public interface IRenderableModule
{
  RenderableModuleDescriptor Descriptor { get; }

  // --- Static geometry (persistent, uploaded once) ---
  IDrawHandle AllocateStatic(ReadOnlySpan<byte> vertices, ReadOnlySpan<uint> indices);
  IDrawHandle AllocateStatic(ReadOnlySpan<byte> vertices);

  // --- Draw submission (recorded into the pending draw list) ---
  void DrawStatic(IDrawHandle handle, ReadOnlySpan<byte> uniformData, ReadOnlySpan<IGpuTexture> textures);
  void DrawDynamic(ReadOnlySpan<byte> vertices, ReadOnlySpan<uint> indices, ReadOnlySpan<byte> uniformData, ReadOnlySpan<IGpuTexture> textures);
  void DrawDynamic(ReadOnlySpan<byte> vertices, ReadOnlySpan<byte> uniformData, ReadOnlySpan<IGpuTexture> textures);
}
