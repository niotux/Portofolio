using QuantiX.Engine.Audio.Linux;

namespace QuantiX.Engine.Audio;

public sealed class AudioService : IAppService
{
  private readonly ALSAAudioPlayerServer server;

  public AudioService(AudioFormat format, int bufferSampleCount = 1024)
  {
    if (OperatingSystem.IsLinux())
    {
      this.server = new ALSAAudioPlayerServer(in format, bufferSampleCount);
    }
    else throw new PlatformNotSupportedException("Audio is not supported on this operating system.");
  }



  public AppServer Server => this.server;
  public IAudioPlayer Player => this.server;
}
