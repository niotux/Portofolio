namespace QuantiX.Engine.Rendering;

public interface IShader : IGPUObject
{
  public ShaderProgram[] Programs { get; }
}
