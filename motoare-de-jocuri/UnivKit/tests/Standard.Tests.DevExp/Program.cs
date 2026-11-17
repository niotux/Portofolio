using UnivKit.Application;
using UnivKit.OS.Audio;
using UnivKit.OS.Windowing;

internal static class Program
{
  public static async Task Main()
  {
    Host host = new();
    var windowing = host.AddSingleton<WindowingService>();

    host.StartAll();

    var win = windowing.CreateWindow("Vlad", 800, 400);
    win.Open();
    Console.WriteLine(windowing.Backend);
    win.OnCloseConfirmed += () => host.StopAll();

    await host.WaitForStop();
  }
}