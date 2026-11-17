using UnivKit.Audio;

namespace UnivKit.OS.Audio;

public interface IAudioManager
{
  void Bind(IAudioSource source);
}