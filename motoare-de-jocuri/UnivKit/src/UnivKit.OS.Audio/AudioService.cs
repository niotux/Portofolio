using UnivKit.Application;
using UnivKit.Audio;

namespace UnivKit.OS.Audio;

public class AudioService : IService, IAudioManager
{
  private readonly AudioNativeServiceBase service;
  public AudioService()
  {
    if (OperatingSystem.IsLinux())
    {
      // this.service = new ALSA.AudioNativeService();
    }

    throw new PlatformNotSupportedException("Audio is not supported on this operating system.");
  }

  public Task Execute(CancellationToken cancellationToken)
  {
    throw new NotImplementedException();
  }

  public void Bind(IAudioSource source)
  {
    throw new NotImplementedException();
  }
}