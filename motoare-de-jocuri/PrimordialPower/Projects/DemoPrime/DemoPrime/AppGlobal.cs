using System;
using PrimordialPower.Platform;

namespace DemoPrime;

public static class AppGlobal
{
  public static Viewport Viewport { get; } = new("DemoPrime", 1000, 700);

  public static class Servers
  {
    public static MainServer MainServer { get; } = new();
    public static ViewportServer ViewportServer { get; } = new();
    public static AudioServer AudioServer { get; } = new(AudioFormat.Stereo16Bit);
  }

  public static class AudioLib
  {
    public static Mp3AudioSource MainTheme { get; } = new("../../../assets/Exile - OST.mp3");
    public static Mp3AudioSource Sound { get; } = new("../../../assets/Exile - OST.mp3");
    public static Mp3AudioSource Theme2 { get; } = new("../../../assets/Exile - OST.mp3");
    public static Mp3AudioSource BattleTheme { get; } = new("../../../assets/Exile - OST.mp3");
    public static Mp3AudioSource HeroTheme { get; } = new("../../../assets/Exile - OST.mp3");
  }



  static AppGlobal()
  {
    Servers.ViewportServer.Manager.Bind(Viewport);
    Servers.AudioServer.Player.Bind(AudioLib.MainTheme);
    Servers.AudioServer.Player.Begin();
  }
}
