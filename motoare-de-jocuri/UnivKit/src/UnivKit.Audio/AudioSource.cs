using System.Diagnostics.Metrics;
using UnivKit.Audio;
using UnivKit.Utility.Async;

namespace UnivKit.Audio;

public sealed class AudioSource : IAudioSource, IDisposable
{
  private readonly Stream source;
  public AudioSource(Stream source, AudioFormat format)
  {
    this.source = source;
    this.Format = format;
  }

  public void Dispose()
  {
    this.source.Dispose();
  }

  private AudioState state = AudioState.Paused;
  public AudioState State => this.state;

  public AudioFormat Format { get; }

  private readonly AsyncGate playingGate = new();
  public Task FinishPlaying => this.playingGate.WaitToOpenAsync().AsTask();

  private double volume;
  public double Volume
  {
    get => volume;
    set
    {
      Interlocked.Exchange(ref this.volume, double.Clamp(value, 0.0, 1.0));
    }
  }

  public long Length => this.source.Length;
  public long Position
  {
    get => (long)this.source.Position;
    set => this.source.Position = value;
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

  internal void MarkFinished()
  {
    this.playingGate.Open();
    this.state = AudioState.Finished;
  }

  public int Read(Span<byte> buffer)
  {
    return this.source.Read(buffer);
  }

  public void Pause()
  {
    if (this.state == AudioState.Finished)
    {
      return;
    }
    this.state = AudioState.Paused;
  }

  public void Play()
  {
    if (this.state == AudioState.Finished)
    {
      return;
    }
    this.state = AudioState.Playing;
  }

  public void Reset()
  {
    this.source.Position = 0;
    this.state = AudioState.Paused;
  }

  public long Seek(long sample, SeekOrigin origin = SeekOrigin.Begin)
  {
    long position = sample * this.Format.BytesPerSample;
    this.source.Seek(position, origin);
    return sample;
  }

  public long Seek(TimeSpan timeStamp, SeekOrigin origin = SeekOrigin.Begin)
  {
    long sample = (long)(timeStamp.TotalSeconds * this.Format.SampleRate);
    return this.Seek(sample, origin);
  }
}