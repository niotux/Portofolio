namespace QuantiX.Engine.RenderingTMP;

public readonly struct BlendState
{
  public bool Enabled { get; init; }
  public BlendFactor SrcFactor { get; init; }
  public BlendFactor DstFactor { get; init; }
  public BlendFactor SrcAlphaFactor { get; init; }
  public BlendFactor DstAlphaFactor { get; init; }

  public static readonly BlendState Disabled = new()
  {
    Enabled = false,
    SrcFactor = BlendFactor.One,
    DstFactor = BlendFactor.Zero,
    SrcAlphaFactor = BlendFactor.One,
    DstAlphaFactor = BlendFactor.Zero,
  };

  public static readonly BlendState AlphaBlend = new()
  {
    Enabled = true,
    SrcFactor = BlendFactor.SrcAlpha,
    DstFactor = BlendFactor.OneMinusSrcAlpha,
    SrcAlphaFactor = BlendFactor.One,
    DstAlphaFactor = BlendFactor.OneMinusSrcAlpha,
  };

  public static readonly BlendState Additive = new()
  {
    Enabled = true,
    SrcFactor = BlendFactor.One,
    DstFactor = BlendFactor.One,
    SrcAlphaFactor = BlendFactor.One,
    DstAlphaFactor = BlendFactor.One,
  };
}
