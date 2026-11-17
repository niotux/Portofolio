namespace QuantiX.Engine.RenderingTMP;

public readonly struct DepthState
{
  public bool TestEnabled { get; init; }
  public bool WriteEnabled { get; init; }
  public DepthFunc Func { get; init; }

  /// <summary>No depth testing or writing.</summary>
  public static readonly DepthState Disabled = new()
  {
    TestEnabled = false,
    WriteEnabled = false,
    Func = DepthFunc.Always,
  };

  /// <summary>Standard depth test — write on pass, discard if farther.</summary>
  public static readonly DepthState Default = new()
  {
    TestEnabled = true,
    WriteEnabled = true,
    Func = DepthFunc.Less,
  };

  /// <summary>Test but do not write — suitable for transparent objects.</summary>
  public static readonly DepthState ReadOnly = new()
  {
    TestEnabled = true,
    WriteEnabled = false,
    Func = DepthFunc.Less,
  };
}
