using Terminal.Gui.Drawing;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Dtm.Ui;

/// <summary>htop-like palette.</summary>
public static class Theme
{
  public static readonly Color Bg = new(0x12, 0x12, 0x12);
  public static readonly Color Text = new(0xd0, 0xd0, 0xd0);
  public static readonly Color Dim = new(0x6c, 0x6c, 0x6c);
  public static readonly Color Green = new(0x5f, 0xd7, 0x5f);
  public static readonly Color Red = new(0xff, 0x5f, 0x5f);
  public static readonly Color Yellow = new(0xff, 0xd7, 0x5f);
  public static readonly Color Cyan = new(0x5f, 0xaf, 0xd7);
  public static readonly Color HeaderBg = new(0x00, 0xaf, 0x5f);
  public static readonly Color SelectBg = new(0x00, 0xaf, 0xd7);
  public static readonly Color Black = new(0x00, 0x00, 0x00);
  public static readonly Color White = new(0xff, 0xff, 0xff);

  public static readonly Attribute Normal = new(Text, Bg);
  public static readonly Attribute Faint = new(Dim, Bg);
  public static readonly Attribute Label = new(Cyan, Bg);
  public static readonly Attribute Header = new(Black, HeaderBg);
  public static readonly Attribute KeyName = new(White, Black);
  public static readonly Attribute KeyText = new(Black, SelectBg);
  public static readonly Attribute Error = new(Red, Bg);
  public static readonly Attribute Ok = new(Green, Bg);
  public static readonly Attribute Warn = new(Yellow, Bg);
}
