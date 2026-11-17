using System.Drawing;
using System.Threading.Channels;
using OpenUL.Assets;
using OpenUL.Assets.Media.Common;
using OpenUL.Assets.Media.STB;
using OpenUL.Manshell;
using OpenUL.Manshell.Common;
using OpenUL.Mathematics.Vectors;
using OpenUL.Windowing.Desktop;
using OpenUL.Windowing.Events;
using OpenUL.Windowing.GLFW;

namespace Game;

internal static class Program
{
  public static async Task Main()
  {
    {
      var image = PixelImage<RGBAPixel<byte>>.Create(1000, 1000);
      for (int i = 0; i < 1000; i++)
      {
        image[i, i] = new()
        {
          Red = (byte)(byte.MaxValue * (i + 1) / 1000),
          Green = (byte)(byte.MaxValue * (1000 - i) / 1000),
          Blue = 0,
          Alpha = byte.MaxValue,
        };
      }
      using var file = File.Open("Mandelbrot.png", FileMode.Create);
      STBImage.Save(file, image);
    }

    using var backend = DesktopWindowingBackend.Select(
      GLFWWindowingBackend.Instance
    );

    var win = InitWindow(backend);
    var win2 = InitWindow(backend);

    (string, object?)[] vars =
    [
      (nameof(backend), backend),
      (nameof(win), win),
      (nameof(win2), win2),
    ];

    var manshell = Task.Run(() => Shell.Run(vars));
    var rendering1 = Task.Run(() => Rendering.Run(win));
    var rendering2 = Task.Run(() => Rendering.Run(win2));

    while (win.IsOpen || win2.IsOpen)
    {
      backend.WaitEvents();
    }

    await rendering1;
    await rendering2;
    await manshell;
  }

  private static IDesktopWindow InitWindow(IDesktopWindowingBackend<IDesktopWindow> backend)
  {
    var win = backend.OpenWindow(new()
    {
      Size = (800, 600),
      Title = "Game",
      Resizable = true,
    });


    win.Closing += (out close) =>
    {
      Console.WriteLine("Window is closing... it should close any minute now");
      close = true;
    };

    win.Closed += () =>
    {
      Console.WriteLine("Window is closed. done... bye");
    };

    win.Show();
    return win;
  }
}
