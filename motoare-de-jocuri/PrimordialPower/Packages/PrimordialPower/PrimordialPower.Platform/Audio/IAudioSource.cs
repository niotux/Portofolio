namespace PrimordialPower.Platform;

public interface IAudioSource
{
  AudioFormat Format { get; }
  AudioState State { get; }

  void Play();
  void Pause();
  void Reset();
  int Read(Span<byte> buffer);
}