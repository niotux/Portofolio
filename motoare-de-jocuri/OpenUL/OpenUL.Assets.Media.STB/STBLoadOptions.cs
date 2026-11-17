namespace OpenUL.Assets.Media.STB;

public readonly struct STBLoadOptions
{
  public STBChannelFormat ChannelFormat { get; init; }
  public STBChannels Channels { get; init; }

  public static readonly STBLoadOptions Default = new()
  {
    Channels = STBChannels.Default,
    ChannelFormat = STBChannelFormat.Default,
  };
}
