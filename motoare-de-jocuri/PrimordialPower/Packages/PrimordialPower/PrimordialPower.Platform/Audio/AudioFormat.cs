using System;

namespace PrimordialPower.Platform;

public record AudioFormat
{
  private readonly AudioSampleFormat sampleFormat;
  public required AudioSampleFormat SampleFormat
  {
    get => this.sampleFormat;
    init
    {
      this.sampleFormat = value;
      this.bytesPerSample = value switch
      {
        AudioSampleFormat.Int8 or
        AudioSampleFormat.UInt8 => 1,
        AudioSampleFormat.Int16_LE or
        AudioSampleFormat.UInt16_LE or
        AudioSampleFormat.Int16_BE or
        AudioSampleFormat.UInt16_BE => 2,
        AudioSampleFormat.Int24_LE or
        AudioSampleFormat.UInt24_LE or
        AudioSampleFormat.Int24_BE or
        AudioSampleFormat.UInt24_BE or
        AudioSampleFormat.Int32_LE or
        AudioSampleFormat.UInt32_LE or
        AudioSampleFormat.Int32_BE or
        AudioSampleFormat.UInt32_BE or
        AudioSampleFormat.Single_LE or
        AudioSampleFormat.Single_BE => 4,
        AudioSampleFormat.Double_LE or
        AudioSampleFormat.Double_BE => 8,
        _ => throw new ArgumentOutOfRangeException()
      };
    }
  }

  private readonly int channels;
  public required int Channels
  {
    get => this.channels;
    init => this.channels = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), "Cannot have negative or zero channels.");
  }

  private readonly int sampleRate;
  public required int SampleRate
  {
    get => this.sampleRate;
    init => this.sampleRate = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), "Cannot have a negative or zero sample rate.");
  }



  private readonly int bytesPerSample;
  public int BytesPerSample => this.bytesPerSample;
  public int BytesPerFrame => checked(this.BytesPerSample * this.Channels);



  public static AudioFormat Stereo16Bit { get; } = new()
  {
    Channels = 2,
    SampleFormat = AudioSampleFormat.Int16_LE,
    SampleRate = 48000
  };

  public static AudioFormat Stereo8Bit { get; } = new()
  {
    Channels = 2,
    SampleFormat = AudioSampleFormat.Int8,
    SampleRate = 44100
  };
}
