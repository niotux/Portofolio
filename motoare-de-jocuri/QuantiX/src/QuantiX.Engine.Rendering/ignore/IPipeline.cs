namespace QuantiX.Engine.Rendering_IGNORE;

/// <summary>
/// A configured rendering unit — shader + vertex layout + pipeline state + batched geometry.
/// Created by IRenderer.CreatePipeline — do not instantiate directly.
///
/// Static geometry (IMesh) is uploaded once and reused across frames.
/// Dynamic geometry is passed inline per draw and is discarded after End().
/// </summary>
public interface IPipeline : IDisposable
{
  IMesh Upload(ReadOnlySpan<byte> vertices, ReadOnlySpan<uint> indices);
  IMesh Upload(ReadOnlySpan<byte> vertices);

  void Draw(IMesh mesh, ReadOnlySpan<byte> uniforms, ReadOnlySpan<IGpuTexture2D> textures = default);

  // Dynamic draw — geometry provided inline, lives only for this frame
  void Draw(ReadOnlySpan<byte> vertices, ReadOnlySpan<uint> indices, ReadOnlySpan<byte> uniforms, ReadOnlySpan<IGpuTexture2D> textures = default);
  void Draw(ReadOnlySpan<byte> vertices, ReadOnlySpan<byte> uniforms, ReadOnlySpan<IGpuTexture2D> textures = default);
}
