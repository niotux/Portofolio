using System.Diagnostics.Metrics;
using OpenUL.Audio;
using OpenUL.Utility.Async;

namespace OpenUL.Audio;

public sealed class AudioSource : IAudioSource, IDisposable
{
  private readonly Stream Source;
  public AudioSource(Stream source, AudioFormat format)
  {
    this.Source = source;
    this.Format = format;
  }

  public void Dispose()
  {
    this.Source.Dispose();
  }

  public AudioState State { get; private set; }
  public AudioFormat Format { get; }

  private readonly AsyncGate playingGate = new();
  public Task FinishPlaying => this.playingGate.WaitToOpenAsync().AsTask();

  public double Volume
  {
    get;
    set
    {
      Interlocked.Exchange(ref field, double.Clamp(value, 0.0, 1.0));
    }
  }

  public long Length => this.Source.Length;
  public long Position
  {
    get => (long)this.Source.Position;
    set => this.Source.Position = value;
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
    this.State = AudioState.Finished;
  }

  public int Read(Span<byte> buffer)
  {
    return this.Source.Read(buffer);
  }

  public void Pause()
  {
    if (this.State == AudioState.Finished)
    {
      return;
    }
    this.State = AudioState.Paused;
  }

  public void Play()
  {
    if (this.State == AudioState.Finished)
    {
      return;
    }
    this.State = AudioState.Playing;
  }

  public void Reset()
  {
    this.Source.Position = 0;
    this.State = AudioState.Paused;
  }

  public long Seek(long sample, SeekOrigin origin = SeekOrigin.Begin)
  {
    long position = sample * this.Format.BytesPerSample;
    this.Source.Seek(position, origin);
    return sample;
  }

  public long Seek(TimeSpan timeStamp, SeekOrigin origin = SeekOrigin.Begin)
  {
    long sample = (long)(timeStamp.TotalSeconds * this.Format.SampleRate);
    return this.Seek(sample, origin);
  }
}