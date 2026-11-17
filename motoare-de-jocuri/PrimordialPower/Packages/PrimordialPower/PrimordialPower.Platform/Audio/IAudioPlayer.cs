namespace PrimordialPower.Platform;

public interface IAudioPlayer
{
  AudioFormat Format { get; }

  void Begin();
  void Bind(IAudioSource audio);
  void Play(IAudioSource audio);
  void Pause(IAudioSource audio);
  void PlayFull(IAudioSource audio);
  Task PlayFullAsync(IAudioSource audio);
  void Beep(double frequency, double duration, double volume);
  Task BeepAsync(double frequency, double duration, double volume);
}