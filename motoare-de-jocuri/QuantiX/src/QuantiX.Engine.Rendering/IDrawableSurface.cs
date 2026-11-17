using QuantiX.Engine.Windowing;

namespace QuantiX.Engine.Rendering;

public interface IDrawableSurface : ISurface
{
  public void Draw(Pipeline[] pipelines);
}