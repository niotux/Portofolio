namespace QuantiX.Engine.RenderingTMP;

public readonly struct RasterizerState
{
  public CullMode CullMode { get; init; }
  public FrontFace FrontFace { get; init; }
  public FillMode FillMode { get; init; }

  public static readonly RasterizerState Default = new()
  {
    CullMode = CullMode.Back,
    FrontFace = FrontFace.CounterClockwise,
    FillMode = FillMode.Solid,
  };

  public static readonly RasterizerState NoCull = new()
  {
    CullMode = CullMode.None,
    FrontFace = FrontFace.CounterClockwise,
    FillMode = FillMode.Solid,
  };

  public static readonly RasterizerState Wireframe = new()
  {
    CullMode = CullMode.None,
    FrontFace = FrontFace.CounterClockwise,
    FillMode = FillMode.Wireframe,
  };
}
