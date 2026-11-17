namespace UnivKit.Audio;

public interface IAudioSource
{
  AudioState State { get; }
  AudioFormat Format { get; }
  Task FinishPlaying { get; }

  double Volume { get; set; }

  long Length { get; }
  long Position { get; set; }

  long SampleCount { get; }
  long CurrentSample { get; set; }

  TimeSpan Duration { get; }
  TimeSpan CurrentTimestamp { get; set; }

  int Read(Span<byte> samples);
  long Seek(long sample, SeekOrigin origin = SeekOrigin.Begin);
  long Seek(TimeSpan timeStamp, SeekOrigin origin = SeekOrigin.Begin);
  void Play();
  void Pause();
  void Reset();
}