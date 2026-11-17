using System;
using Texp.Fs;
using Terminal.Gui.Drawing;

namespace Texp.Ui;

public readonly record struct Icon
(
  char Glyph,
  Color Color
);

/// <summary>
/// The glyph in front of each name. Plain Unicode that every monospace font has (no Nerd Font needed) and none with
/// an emoji form, so columns stay aligned. To add a file type, add a row to <see cref="Kinds"/>.
/// </summary>
public static class Icons
{
  public static readonly Icon Parent = new('↰', Theme.Blue);
  public static readonly Icon Folder = new('■', Theme.Blue);
  public static readonly Icon Link = new('↪', Theme.Cyan);
  public static readonly Icon Executable = new('❯', Theme.Green);
  public static readonly Icon File = new('□', Theme.Dim);
  public static readonly Icon Marked = new('●', Theme.Yellow);

  /// <summary>A file type: its icon and the extensions (lowercase, no dot) that have it.</summary>
  readonly record struct Kind
  (
    Icon Icon,
    string[] Extensions
  );

  static readonly Kind[] Kinds =
  [
    new(new Icon('≡', Theme.Text), ["txt", "md", "markdown", "rst", "log", "csv", "tsv", "ini", "cfg", "conf", "toml", "yaml", "yml", "json", "xml"]),
    new(new Icon('λ', Theme.Yellow), ["cs", "c", "h", "cpp", "hpp", "cc", "rs", "go", "py", "js", "ts", "tsx", "jsx", "java", "kt", "lua", "rb", "php", "sh", "bash", "zsh", "fish", "html", "css", "scss", "sql", "csproj", "sln", "slnx", "zig", "swift", "hs", "ml", "fs"]),
    new(new Icon('▣', Theme.Magenta), ["png", "jpg", "jpeg", "gif", "bmp", "webp", "svg", "ico", "tif", "tiff", "heic", "avif", "xcf", "psd", "kra"]),
    new(new Icon('♪', Theme.Magenta), ["mp3", "flac", "ogg", "opus", "wav", "m4a", "aac", "wma"]),
    new(new Icon('►', Theme.Magenta), ["mp4", "mkv", "webm", "avi", "mov", "wmv", "m4v", "flv"]),
    new(new Icon('▦', Theme.Red), ["zip", "tar", "gz", "tgz", "xz", "bz2", "zst", "7z", "rar", "deb", "rpm", "iso", "img", "appimage", "jar", "nupkg"]),
    new(new Icon('¶', Theme.Sand), ["pdf", "odt", "ods", "odp", "odg", "odf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "rtf", "epub", "djvu"]),
  ];

  public static Icon For(Entry e)
  {
    if (e.IsParent)
    {
      return Parent;
    }
    if (e.IsDir)
    {
      return Folder;
    }
    if (e.IsLink)
    {
      return Link;
    }
    // Runnable beats file type: a script you can run shows as runnable.
    if (e.IsExecutable)
    {
      return Executable;
    }
    ReadOnlySpan<char> name = e.Name;
    int dot = name.LastIndexOf('.');
    if (dot > 0 && dot < name.Length - 1)
    {
      ReadOnlySpan<char> ext = name.Slice(dot + 1);
      for (int k = 0; k < Kinds.Length; k++)
      {
        string[] exts = Kinds[k].Extensions;
        for (int i = 0; i < exts.Length; i++)
        {
          if (ext.Equals(exts[i], StringComparison.OrdinalIgnoreCase))
          {
            return Kinds[k].Icon;
          }
        }
      }
    }
    return File;
  }
}
