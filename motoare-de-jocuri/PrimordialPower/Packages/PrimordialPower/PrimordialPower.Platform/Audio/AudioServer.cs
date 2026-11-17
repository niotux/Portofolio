using System;
using PrimordialPower.Runtime;

namespace PrimordialPower.Platform;

public class AudioServer : AppServer
{
  private readonly IAudioPlayer player;
  private readonly AppServer server;
  public AudioServer(AudioFormat format, int bufferSampleCount = 1024)
  {
    if (OperatingSystem.IsWindows())
    {
      // Windows.Win32AudioPlayerServer playerServer = new(format);
      // this.player = playerServer;
      // this.server = playerServer;
      throw new Exception("Audio on this operating system not supported");
    }
    else if (OperatingSystem.IsLinux())
    {
      Linux.ALSAAudioPlayerServer playerServer = new(format, bufferSampleCount);
      this.player = playerServer;
      this.server = playerServer;
    }
    else throw new Exception("Audio on this operating system not supported");
  }



  public IAudioPlayer Player => this.player;
  public override AppServerLifetime Lifetime => this.server.Lifetime;
  public override void Init() => this.server.Init();
  public override Task RunAsync() => this.server.RunAsync();
  public override bool Loop() => this.server.Loop();
  public override void Shutdown() => this.server.Shutdown();
  public override void OnStop() => this.server.OnStop();
}
