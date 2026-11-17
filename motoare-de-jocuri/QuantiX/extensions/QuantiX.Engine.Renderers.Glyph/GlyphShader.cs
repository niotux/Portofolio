using QuantiX.Engine.Rendering;

namespace QuantiX.Engine.Renderers.Glyph;

internal sealed class GlyphShader : IShader
{
  private static ulong _nextHandle = 1;

  private readonly ulong _handle;

  public ShaderProgram[] Programs { get; }
  public GPUObjectHandle ApiHandle => new() { ApiHandleValue = _handle };

  internal GlyphShader(ShaderProgram[] programs)
  {
    _handle = _nextHandle++;
    Programs = programs;
  }

  public void Dispose() { }
}
