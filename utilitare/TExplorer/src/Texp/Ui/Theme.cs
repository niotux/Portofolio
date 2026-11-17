using Terminal.Gui.Drawing;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Texp.Ui;

/// <summary>Retro palette, shared look with dtm.</summary>
public static class Theme
{
  public static readonly Color Bg = new(0x12, 0x12, 0x12);
  public static readonly Color PanelBg = new(0x1c, 0x1c, 0x1c);
  public static readonly Color Text = new(0xd0, 0xd0, 0xd0);
  public static readonly Color Dim = new(0x6c, 0x6c, 0x6c);
  public static readonly Color Green = new(0x5f, 0xd7, 0x5f);
  public static readonly Color Red = new(0xff, 0x5f, 0x5f);
  public static readonly Color Yellow = new(0xff, 0xd7, 0x5f);
  public static readonly Color Cyan = new(0x5f, 0xd7, 0xd7);
  public static readonly Color Blue = new(0x6c, 0xb6, 0xff);
  public static readonly Color Magenta = new(0xd7, 0x87, 0xd7);
  public static readonly Color Sand = new(0xd7, 0xba, 0x7d);
  public static readonly Color HeaderBg = new(0x00, 0xaf, 0x87);
  public static readonly Color SelectBg = new(0x26, 0x4f, 0x78);
  public static readonly Color TabBg = new(0x2a, 0x2a, 0x40);
  public static readonly Color Black = new(0x00, 0x00, 0x00);
  public static readonly Color White = new(0xff, 0xff, 0xff);

  public static readonly Attribute Normal = new(Text, Bg);
  public static readonly Attribute Faint = new(Dim, Bg);
  public static readonly Attribute Folder = new(Blue, Bg);
  public static readonly Attribute Link = new(Cyan, Bg);
  public static readonly Attribute Exec = new(Green, Bg);
  public static readonly Attribute Marked = new(Yellow, Bg);
  public static readonly Attribute Cursor = new(White, SelectBg);
  public static readonly Attribute CursorMarked = new(Yellow, SelectBg);
  public static readonly Attribute Header = new(Black, HeaderBg);
  public static readonly Attribute Path = new(Sand, Bg);
  public static readonly Attribute PathHere = new(White, Bg);
  public static readonly Attribute Side = new(Sand, PanelBg);
  public static readonly Attribute SideText = new(Text, PanelBg);
  public static readonly Attribute SideFaint = new(Dim, PanelBg);
  public static readonly Attribute SideHere = new(White, SelectBg);
  public static readonly Attribute TabOn = new(White, TabBg);
  public static readonly Attribute TabOff = new(Dim, Bg);
  public static readonly Attribute Prompt = new(Cyan, Bg);
  public static readonly Attribute Command = new(Green, Bg);
  public static readonly Attribute FilterText = new(Yellow, Bg);
  public static readonly Attribute PathText = new(Cyan, Bg);
  public static readonly Attribute Caret = new(Black, Text);
  public static readonly Attribute KeyName = new(Black, Text);
  public static readonly Attribute KeyText = new(Text, Bg);
  public static readonly Attribute Error = new(Red, Bg);
  public static readonly Attribute Ok = new(Green, Bg);
  public static readonly Attribute Warn = new(Yellow, Bg);
  public static readonly Attribute Box = new(Text, PanelBg);
  public static readonly Attribute BoxBorder = new(HeaderBg, PanelBg);
  public static readonly Attribute BoxFaint = new(Dim, PanelBg);
  public static readonly Attribute BoxPick = new(White, SelectBg);
}
