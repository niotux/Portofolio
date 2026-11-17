namespace OpenUL.Assets.Media.STB;

public readonly struct STBSaveOptions
{
  public STBFileFormat Format { get; init; }
  public STBQuality Quality { get; init; }

  public static readonly STBSaveOptions Default = new()
  {
    Format = STBFileFormat.PNG,
    Quality = STBQuality.FromPercentage(100),
  };
}