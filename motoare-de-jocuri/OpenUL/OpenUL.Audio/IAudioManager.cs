using OpenUL.Audio;

namespace OpenUL.OS.Audio;

public interface IAudioManager
{
  void Bind(IAudioSource source);
}