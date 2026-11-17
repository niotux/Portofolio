namespace QuantiX.Engine.Audio;

public interface IAudioPlayer
{
  AudioFormat Format { get; }

  void Bind(IAudioSource audio);
  void Play(IAudioSource audio);
  void Pause(IAudioSource audio);
  void PlayFull(IAudioSource audio);
  Task PlayFullAsync(IAudioSource audio);
  void Beep(double frequency, double duration, double volume);
  Task BeepAsync(double frequency, double duration, double volume);
}