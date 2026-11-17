using OpenUL.Windowing;

namespace Game;

public static class Rendering
{
  public static void Run(IWindow window)
  {
    while (window.UpdateClient())
    {
      if (window.IsVisible)
      {
        //Console.WriteLine(window.Size);
      }
    }
  }
}