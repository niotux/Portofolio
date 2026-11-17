using System;
using System.Globalization;
using System.IO;

namespace Texp.Fs;

/// <summary>Sizes, dates and permissions as short text, written into caller-owned spans.</summary>
public static class Format
{
  static readonly string[] Months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

  /// <summary>"812", "4.1 K", "12 M", "3.4 G".</summary>
  public static int Bytes(long bytes, Span<char> into)
  {
    int n;
    if (bytes < 1024)
    {
      bytes.TryFormat(into, out n, default, CultureInfo.InvariantCulture);
      return n;
    }
    double v = bytes;
    char unit = 'K';
    v /= 1024;
    if (v >= 1024)
    {
      v /= 1024;
      unit = 'M';
    }
    if (v >= 1024)
    {
      v /= 1024;
      unit = 'G';
    }
    if (v >= 1024)
    {
      v /= 1024;
      unit = 'T';
    }
    v.TryFormat(into, out n, v < 10 ? "0.0" : "0", CultureInfo.InvariantCulture);
    into[n++] = ' ';
    into[n++] = unit;
    return n;
  }

  public static string Bytes(long bytes)
  {
    Span<char> buf = stackalloc char[24];
    return new string(buf.Slice(0, Bytes(bytes, buf)));
  }

  /// <summary>"today 14:02", "12 Oct", "12 Oct 2024" relative to <paramref name="now"/>.</summary>
  public static int Date(DateTime t, DateTime now, Span<char> into)
  {
    int n;
    if (t == default)
    {
      into[0] = '?';
      return 1;
    }
    if (t.Date == now.Date)
    {
      "today ".AsSpan().CopyTo(into);
      t.TryFormat(into.Slice(6), out n, "HH:mm", CultureInfo.InvariantCulture);
      return 6 + n;
    }
    t.Day.TryFormat(into, out n, default, CultureInfo.InvariantCulture);
    into[n++] = ' ';
    Months[t.Month - 1].AsSpan().CopyTo(into.Slice(n));
    n += 3;
    if (t.Year != now.Year)
    {
      into[n++] = ' ';
      t.Year.TryFormat(into.Slice(n), out int y, default, CultureInfo.InvariantCulture);
      n += y;
    }
    return n;
  }

  /// <summary>"rwxr-xr-x".</summary>
  public static int Mode(UnixFileMode mode, Span<char> into)
  {
    into[0] = (mode & UnixFileMode.UserRead) != 0 ? 'r' : '-';
    into[1] = (mode & UnixFileMode.UserWrite) != 0 ? 'w' : '-';
    into[2] = (mode & UnixFileMode.UserExecute) != 0 ? 'x' : '-';
    into[3] = (mode & UnixFileMode.GroupRead) != 0 ? 'r' : '-';
    into[4] = (mode & UnixFileMode.GroupWrite) != 0 ? 'w' : '-';
    into[5] = (mode & UnixFileMode.GroupExecute) != 0 ? 'x' : '-';
    into[6] = (mode & UnixFileMode.OtherRead) != 0 ? 'r' : '-';
    into[7] = (mode & UnixFileMode.OtherWrite) != 0 ? 'w' : '-';
    into[8] = (mode & UnixFileMode.OtherExecute) != 0 ? 'x' : '-';
    return 9;
  }
}
