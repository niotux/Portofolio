using System.Text;
using QuantiX.Engine.Assets;
using QuantiX.Engine.Assets.Common;

internal static class Program
{
  public static void Main()
  {
    IAssetsProvider assets = new DiskAssets("assets");

    var image = assets.LoadImageFile("floricica dansatoare.png");

    assets.CreateFile("save.Image");
    assets.SaveImage("save.Image", image);

    var loaded = assets.LoadImage("save.Image");
    image = loaded;

    TextWriter writer = new StringWriter();

    for (uint x = 0; x < image.Height; x++)
    {
      for (uint y = 0; y < image.Width; y++)
      {
        uint pixelBase = (y + x * image.Width) * image.ColorChannels;
        WriteColored(writer, '#', image.Data[pixelBase], image.Data[pixelBase + 1], image.Data[pixelBase + 2]);
      }
      writer.WriteLine();
    }

    Console.WriteLine(writer.ToString());
  }

  private static void WriteColored(TextWriter writer, char c, byte r, byte g, byte b)
  {
    writer.Write("\x1b[38;2;");
    writer.Write(r);
    writer.Write(';');
    writer.Write(g);
    writer.Write(';');
    writer.Write(b);
    writer.Write('m');
    writer.Write(c);
    writer.Write(c);
    writer.Write("\x1b[0m");
  }
}