using System.IO.Enumeration;

namespace OpenUL.Manshell.Common;

public sealed class List_DirCmd : ManshellCmd
{
  public override Type[] Args { get; } =
  [
    typeof(string),
  ];
  public override string[] Names { get; } = ["list-dir", "ls", "dir"];
  public override int MinimumArgs => 0;

  private const int NameWidth = 30;
  private const int SizeWidth = 11;
  private const int ModifiedWidth = 22;
  private const int CreatedWidth = 22;

  protected override object? OnRun(ManshellContext context, ReadOnlySpan<ManshellArg> args)
  {
    context.Console.Foreground = ConsoleColor.Green;
    context.Console.StdOut.WriteLine($"{"Name",-NameWidth} {"Size",-SizeWidth} {"Modified",-ModifiedWidth} {"Created",-CreatedWidth}");
    context.Console.ResetColor();

    var pattern = args.Length >= 1
      ? context.GetValue(args[0]) as string ?? string.Empty
      : string.Empty;

    var simple = new EnumerationOptions() { ReturnSpecialDirectories = false, IgnoreInaccessible = true };
    var hidden = new EnumerationOptions() { ReturnSpecialDirectories = true, IgnoreInaccessible = false };

    var dirs = new FileSystemEnumerable<bool>(Directory.GetCurrentDirectory(), (ref entry) =>
    {
      if (!entry.IsDirectory)
      {
        return false;
      }
      context.Console.StdOut.WriteLine($"{entry.FileName,-30} {string.Empty,-11} {entry.CreationTimeUtc.LocalDateTime,-22} {entry.CreationTimeUtc.LocalDateTime,-22}");
      return true;
    }, simple);

    var files = new FileSystemEnumerable<bool>(Directory.GetCurrentDirectory(), (ref entry) =>
    {
      if (entry.IsDirectory)
      {
        return false;
      }
      context.Console.StdOut.WriteLine($"{entry.FileName,-30} {entry.Length,-9} B {entry.CreationTimeUtc.LocalDateTime,-22} {entry.CreationTimeUtc.LocalDateTime,-22}");
      return true;
    }, simple);

    var hidden_files = new FileSystemEnumerable<bool>(Directory.GetCurrentDirectory(), (ref entry) =>
    {
      if (entry.IsDirectory)
      {
        return false;
      }
      if (!entry.IsHidden)
      {
        return false;
      }
      context.Console.StdOut.WriteLine($"{entry.FileName,-30} {entry.Length,-9} B {entry.CreationTimeUtc.LocalDateTime,-22} {entry.CreationTimeUtc.LocalDateTime,-22}");
      return true;
    }, hidden);

    var hidden_dirs = new FileSystemEnumerable<bool>(Directory.GetCurrentDirectory(), (ref entry) =>
    {
      if (!entry.IsDirectory)
      {
        return false;
      }
      if (!entry.IsHidden)
      {
        return false;
      }
      context.Console.StdOut.WriteLine($"{entry.FileName,-30} {string.Empty,-11} {entry.CreationTimeUtc.LocalDateTime,-22} {entry.CreationTimeUtc.LocalDateTime,-22}");
      return true;
    }, hidden);

    context.Console.Foreground = ConsoleColor.Magenta;
    foreach (var _ in hidden_dirs)
    {
    }

    context.Console.Foreground = ConsoleColor.Cyan;
    foreach (var _ in dirs)
    {
    }

    context.Console.Foreground = ConsoleColor.Yellow;
    foreach (var _ in hidden_files)
    {
    }

    context.Console.Foreground = ConsoleColor.White;
    foreach (var _ in files)
    {
    }

    context.Console.ResetColor();
    return null;
  }
}
