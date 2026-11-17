using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Dtm.Docker;

/// <summary>Walks a tab-separated line produced by a docker <c>--format</c> template.</summary>
public ref struct FieldReader
{
  ReadOnlySpan<char> _rest;

  public FieldReader(ReadOnlySpan<char> line)
  {
    _rest = line;
  }

  public ReadOnlySpan<char> Next()
  {
    int tab = _rest.IndexOf('\t');
    if (tab < 0)
    {
      ReadOnlySpan<char> last = _rest;
      _rest = default;
      return last;
    }
    ReadOnlySpan<char> field = _rest.Slice(0, tab);
    _rest = _rest.Slice(tab + 1);
    return field;
  }
}

/// <summary>
/// Parses docker CLI output. Each command is run with a <c>--format</c> template (see <see cref="Formats"/>)
/// that asks for exactly the fields used here, so a line is just fields separated by tabs (or spaces where noted).
/// </summary>
public static class Parsing
{
  public static void Contexts(string output, List<DockerContext> into)
  {
    foreach (ReadOnlySpan<char> line in output.AsSpan().EnumerateLines())
    {
      if (line.IsEmpty)
      {
        continue;
      }
      FieldReader f = new(line);
      ReadOnlySpan<char> name = f.Next();
      bool current = f.Next().SequenceEqual("true");
      ReadOnlySpan<char> error = f.Next();
      into.Add(new DockerContext(new string(name), current, error.IsEmpty ? null : new string(error)));
    }
  }

  public static void Containers(string output, List<Container> into)
  {
    foreach (ReadOnlySpan<char> line in output.AsSpan().EnumerateLines())
    {
      if (line.IsEmpty)
      {
        continue;
      }
      FieldReader f = new(line);
      string id = new(f.Next());
      string name = new(f.Next());
      string image = new(f.Next());
      ContainerState state = State(f.Next());
      string status = new(f.Next());
      ReadOnlySpan<char> project = f.Next();
      into.Add(new Container(id, name, image, state, status, project.IsEmpty ? null : new string(project), 0, 0, -1, false, false));
    }
  }

  /// <summary>Fills Cpu and MemBytes of matching containers from <c>docker stats</c> output.</summary>
  public static void ApplyStats(string output, List<Container> containers)
  {
    Span<Container> span = CollectionsMarshal.AsSpan(containers);
    foreach (ReadOnlySpan<char> line in output.AsSpan().EnumerateLines())
    {
      FieldReader f = new(line);
      int i = IndexOfId(span, f.Next());
      if (i < 0)
      {
        continue;
      }
      span[i].Cpu = Percent(f.Next());
      ReadOnlySpan<char> mem = f.Next();
      int slash = mem.IndexOf('/');
      span[i].MemBytes = Bytes(slash < 0 ? mem : mem.Slice(0, slash));
    }
  }

  /// <summary>Marks which containers requested devices. Input lines: "&lt;full id&gt; &lt;device request count&gt;".</summary>
  public static void ApplyDeviceRequests(string output, List<Container> containers)
  {
    Span<Container> span = CollectionsMarshal.AsSpan(containers);
    foreach (ReadOnlySpan<char> line in output.AsSpan().EnumerateLines())
    {
      int space = line.IndexOf(' ');
      if (space <= 0)
      {
        continue;
      }
      int i = IndexOfId(span, line.Slice(0, space));
      if (i < 0)
      {
        continue;
      }
      span[i].GpuChecked = true;
      span[i].WantsGpu = !line.Slice(space + 1).Trim().SequenceEqual("0");
    }
  }

  public static void Images(string output, List<Image> into)
  {
    foreach (ReadOnlySpan<char> line in output.AsSpan().EnumerateLines())
    {
      if (line.IsEmpty)
      {
        continue;
      }
      FieldReader f = new(line);
      string id = new(f.Next());
      string repository = new(f.Next());
      string tag = new(f.Next());
      string size = new(f.Next());
      string created = new(f.Next());
      into.Add(new Image(id, repository, tag, size, created));
    }
  }

  /// <summary>Parses "&lt;NCPU&gt; &lt;MemTotal&gt;".</summary>
  public static bool Host(string output, out int cpus, out long memTotal)
  {
    ReadOnlySpan<char> s = output.AsSpan().Trim();
    int space = s.IndexOf(' ');
    memTotal = 0;
    cpus = 0;
    return space > 0 && int.TryParse(s.Slice(0, space), NumberStyles.Integer, CultureInfo.InvariantCulture, out cpus) && long.TryParse(s.Slice(space + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out memTotal);
  }

  /// <summary>Parses nvidia-smi utilization output (one value per GPU); multiple GPUs are averaged. -1 when nothing parses.</summary>
  public static double NvidiaUtilization(string output)
  {
    double sum = 0;
    int count = 0;
    foreach (ReadOnlySpan<char> line in output.AsSpan().EnumerateLines())
    {
      if (double.TryParse(line.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
      {
        sum += v;
        count++;
      }
    }
    return count == 0 ? -1 : sum / count;
  }

  public static ContainerState State(ReadOnlySpan<char> s)
  {
    switch (s)
    {
      case "running":
        return ContainerState.Running;
      case "exited":
        return ContainerState.Exited;
      case "created":
        return ContainerState.Created;
      case "paused":
        return ContainerState.Paused;
      case "restarting":
        return ContainerState.Restarting;
      case "removing":
        return ContainerState.Removing;
      case "dead":
        return ContainerState.Dead;
      default:
        return ContainerState.Unknown;
    }
  }

  public static double Percent(ReadOnlySpan<char> s)
  {
    double.TryParse(s.Trim().TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v);
    return v;
  }

  /// <summary>Parses docker's human sizes: "1.676GiB", "640MiB ", "12.5kB", "512B".</summary>
  public static long Bytes(ReadOnlySpan<char> s)
  {
    s = s.Trim();
    int i = 0;
    while (i < s.Length && (char.IsAsciiDigit(s[i]) || s[i] == '.'))
    {
      i++;
    }
    if (!double.TryParse(s.Slice(0, i), NumberStyles.Float, CultureInfo.InvariantCulture, out double n))
    {
      return 0;
    }
    return (long)(n * UnitMultiplier(s.Slice(i).Trim()));
  }

  static double UnitMultiplier(ReadOnlySpan<char> unit)
  {
    switch (unit)
    {
      case "KiB":
        return 1L << 10;
      case "MiB":
        return 1L << 20;
      case "GiB":
        return 1L << 30;
      case "TiB":
        return 1L << 40;
      case "kB":
        return 1e3;
      case "MB":
        return 1e6;
      case "GB":
        return 1e9;
      case "TB":
        return 1e12;
      default:
        return 1;
    }
  }

  /// <summary>Finds a container by ID; either side may be the short (12 char) or full form.</summary>
  static int IndexOfId(Span<Container> containers, ReadOnlySpan<char> id)
  {
    if (id.IsEmpty)
    {
      return -1;
    }
    for (int i = 0; i < containers.Length; i++)
    {
      ReadOnlySpan<char> c = containers[i].Id;
      if (c.StartsWith(id) || id.StartsWith(c))
      {
        return i;
      }
    }
    return -1;
  }
}

/// <summary>The <c>--format</c> templates the parsers above expect.</summary>
public static class Formats
{
  public const string Contexts = "{{.Name}}\t{{.Current}}\t{{.Error}}";
  public const string Containers = "{{.ID}}\t{{.Names}}\t{{.Image}}\t{{.State}}\t{{.Status}}\t{{.Label \"com.docker.compose.project\"}}";
  public const string Stats = "{{.ID}}\t{{.CPUPerc}}\t{{.MemUsage}}";
  public const string Images = "{{.ID}}\t{{.Repository}}\t{{.Tag}}\t{{.Size}}\t{{.CreatedSince}}";
  // inspect and info don't expand \t, so these are space separated; none of the values contain spaces.
  public const string DeviceRequests = "{{.Id}} {{len .HostConfig.DeviceRequests}}";
  public const string Host = "{{.NCPU}} {{.MemTotal}}";
}
