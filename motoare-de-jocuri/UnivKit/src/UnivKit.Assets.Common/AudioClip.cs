using UnivKit.Audio;
using UnivKit.Utility.Async;

namespace UnivKit.Assets.Common;

public sealed class AudioClip : ReadOnlyAudioClip, IAssetLoading<AudioClip, AudioFormat>, IAssetSaving<AudioClip, AudioFormat>
{
  public AudioClip(byte[] data, AudioFormat format)
  : base(data, format)
  { }

  public new byte[] Data => base.data;



  public static new AudioClip LoadAsset(Stream stream)
  {
    using AudioStream audioStream = AudioStream.LoadAsset(stream);
    return DrainToClip(audioStream);
  }

  public static AudioClip LoadAsset(Stream stream, AudioFormat targetFormat)
  {
    using AudioStream audioStream = AudioStream.LoadAsset(stream, targetFormat);
    return DrainToClip(audioStream);
  }

  private static AudioClip DrainToClip(AudioStream audioStream)
  {
    byte[] data = new byte[audioStream.Length];
    int total = 0;
    while (total < data.Length)
    {
      total += audioStream.Read(data.AsSpan(total, data.Length - total));
    }
    return new AudioClip(data, audioStream.Format);
  }

  public static void SaveAsset(Stream stream, AudioClip asset)
    => ReadOnlyAudioClip.SaveAsset(stream, asset);

  public static void SaveAsset(Stream stream, AudioClip asset, AudioFormat targetFormat)
    => ReadOnlyAudioClip.SaveAsset(stream, asset, targetFormat);
}



public class ReadOnlyAudioClip : IAudioSource, IAssetLoading<ReadOnlyAudioClip>, IAssetSaving<ReadOnlyAudioClip, AudioFormat>
{
  public ReadOnlyAudioClip(byte[] data, AudioFormat format)
  {
    this.data = data;
    this.Format = format;
  }

  private protected readonly byte[] data;
  public ReadOnlyMemory<byte> Data => this.data;

  public AudioFormat Format { get; }



  private AudioState state = AudioState.Paused;
  public AudioState State => this.state;

  private readonly AsyncGate playingGate = new();
  public Task FinishPlaying => this.playingGate.WaitToOpenAsync().AsTask();

  private double volume;
  public double Volume
  {
    get => this.volume;
    set
    {
      Interlocked.Exchange(ref this.volume, double.Clamp(value, 0.0, 1.0));
    }
  }

  public long Length => this.data.LongLength;

  private long position;
  public long Position
  {
    get => this.position;
    set => this.position = value;
  }

  public long SampleCount => this.Length / this.Format.BytesPerSample;
  public long CurrentSample
  {
    get => this.Position / this.Format.BytesPerSample;
    set => this.Position = value * this.Format.BytesPerSample;
  }

  public TimeSpan Duration => TimeSpan.FromSeconds((double)this.SampleCount / this.Format.SampleRate);
  public TimeSpan CurrentTimestamp
  {
    get => TimeSpan.FromSeconds((double)this.CurrentSample / this.Format.SampleRate);
    set => this.CurrentSample = (long)(value.TotalSeconds * this.Format.SampleRate);
  }

  public int Read(Span<byte> samples)
  {
    long available = this.Length - this.position;
    if (available <= 0)
    {
      return 0;
    }

    int toCopy = (int)Math.Min(samples.Length, available);
    this.data.AsSpan((int)this.position, toCopy).CopyTo(samples);
    this.position += toCopy;
    return toCopy;
  }

  public long Seek(long sample, SeekOrigin origin = SeekOrigin.Begin)
  {
    long target = origin switch
    {
      SeekOrigin.Begin => sample,
      SeekOrigin.Current => this.CurrentSample + sample,
      SeekOrigin.End => this.SampleCount + sample,
      _ => throw new ArgumentOutOfRangeException(nameof(origin))
    };
    this.CurrentSample = target;
    return target;
  }

  public long Seek(TimeSpan timeStamp, SeekOrigin origin = SeekOrigin.Begin)
  {
    long sample = (long)(timeStamp.TotalSeconds * this.Format.SampleRate);
    return this.Seek(sample, origin);
  }

  public void Play()
  {
    if (this.state == AudioState.Finished)
    {
      return;
    }
    this.state = AudioState.Playing;
  }

  public void Pause()
  {
    if (this.state == AudioState.Finished)
    {
      return;
    }
    this.state = AudioState.Paused;
  }

  public void Reset()
  {
    this.position = 0;
    this.state = AudioState.Paused;
  }



  public static ReadOnlyAudioClip LoadAsset(Stream stream)
    => AudioClip.LoadAsset(stream);

  public static void SaveAsset(Stream stream, ReadOnlyAudioClip asset)
  {
    using (stream)
    {
      stream.Write(asset.data);
    }
  }

  public static void SaveAsset(Stream stream, ReadOnlyAudioClip asset, AudioFormat targetFormat)
  {
    using (stream)
    {
      if (targetFormat != asset.Format)
      {
        throw new NotSupportedException("ReadOnlyAudioClip.SaveAsset only supports raw PCM in the asset's existing format. Resampling/encoding on save is not yet implemented.");
      }
      stream.Write(asset.data);
    }
  }
}
