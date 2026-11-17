namespace OpenUL.Audio;

public record struct AudioFormat
{
  public required uint SampleRate { get; init; }
  private uint channels;
  public required uint Channels
  {
    get => this.channels;
    init
    {
      if (value < 1)
      {
        throw new ArgumentException("You need a positive and non-zero number of channels");
      }
      this.channels = value;
    }
  }
  public required AudioSampleFormat SampleFormat { get; init; }
  public required AudioSampleLayout SampleLayout { get; init; }

  public uint ByteDepth => this.SampleFormat switch
  {
    AudioSampleFormat.Byte => 1,
    AudioSampleFormat.Int16 => 2,
    AudioSampleFormat.Int24 => 3,
    AudioSampleFormat.Int32 => 4,
    AudioSampleFormat.Single => 4,
    AudioSampleFormat.Int64 => 8,
    AudioSampleFormat.Double => 8,
    _ => throw new NotSupportedException($"Unsupported AudioSampleFormat: {this.SampleFormat}")
  };

  public uint BytesPerSample => this.Channels * this.ByteDepth;
}