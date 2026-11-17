using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Dtm.Docker;

/// <summary>
/// Keeps one context's <see cref="ContextSnapshot"/> fresh by polling the docker CLI in the background.
/// The UI only ever reads <see cref="Snapshot"/>, which is replaced (never mutated) on each refresh.
/// </summary>
public sealed class ContextPoller
{
  static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);
  static readonly TimeSpan ErrorInterval = TimeSpan.FromSeconds(5);
  static readonly TimeSpan QuickTimeout = TimeSpan.FromSeconds(10);
  static readonly TimeSpan StatsTimeout = TimeSpan.FromSeconds(20);
  static readonly TimeSpan GpuTimeout = TimeSpan.FromSeconds(5);
  const int ImageEveryNthRefresh = 5;

  public readonly string Name;
  readonly DockerCli _cli;
  readonly CancellationTokenSource _stop = new();
  readonly SemaphoreSlim _kick = new(0, 1);
  volatile ContextSnapshot _snapshot;
  int _cpus;
  long _memTotal;
  int _refreshes;
  bool _imagesWanted;

  public ContextPoller(string name, DockerCli cli)
  {
    Name = name;
    _cli = cli;
    _snapshot = new ContextSnapshot(name, new(), new(), 0, 0, null, false);
  }

  public ContextSnapshot Snapshot
  {
    get { return _snapshot; }
  }

  /// <summary>Replaces the snapshot directly; for tests, which never start the poller.</summary>
  internal void Seed(ContextSnapshot snapshot)
  {
    _snapshot = snapshot;
  }

  public void Start()
  {
    _ = Task.Run(LoopAsync);
  }

  public void Stop()
  {
    _stop.Cancel();
  }

  /// <summary>Refresh now instead of waiting for the next tick (e.g. right after an action).</summary>
  public void Kick(bool includeImages)
  {
    if (includeImages)
    {
      _imagesWanted = true;
    }
    if (_kick.CurrentCount == 0)
    {
      try
      {
        _kick.Release();
      }
      catch (SemaphoreFullException)
      {
        // Already kicked.
      }
    }
  }

  public Task<CliResult> RunAsync(string[] args, TimeSpan timeout)
  {
    return _cli.RunAsync(Name, args, timeout, _stop.Token);
  }

  async Task LoopAsync()
  {
    CancellationToken ct = _stop.Token;
    while (!ct.IsCancellationRequested)
    {
      bool ok;
      try
      {
        ok = await RefreshAsync(ct);
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        Publish(_snapshot.Containers, _snapshot.Images, ex.Message);
        ok = false;
      }
      try
      {
        await _kick.WaitAsync(ok ? Interval : ErrorInterval, ct);
      }
      catch (OperationCanceledException)
      {
        return;
      }
    }
  }

  async Task<bool> RefreshAsync(CancellationToken ct)
  {
    ContextSnapshot previous = _snapshot;
    CliResult ps = await _cli.RunAsync(Name, ["ps", "-a", "--format", Formats.Containers], QuickTimeout, ct);
    if (!ps.Ok)
    {
      Publish(new(), new(), ps.ErrorLine());
      return false;
    }

    List<Container> containers = new();
    Parsing.Containers(ps.Stdout, containers);
    containers.Sort(CompareByProjectThenName);
    CarryOver(previous.Containers, containers);

    bool imagesDue = _imagesWanted || _refreshes % ImageEveryNthRefresh == 0;
    _refreshes++;
    List<Image> images = previous.Images;
    if (imagesDue)
    {
      _imagesWanted = false;
      CliResult img = await _cli.RunAsync(Name, ["images", "--format", Formats.Images], QuickTimeout, ct);
      if (img.Ok)
      {
        images = new();
        Parsing.Images(img.Stdout, images);
      }
    }

    // Show the container list right away; stats can take a couple of seconds.
    Publish(containers, images, null);

    if (_cpus == 0)
    {
      CliResult info = await _cli.RunAsync(Name, ["info", "--format", Formats.Host], QuickTimeout, ct);
      if (info.Ok)
      {
        Parsing.Host(info.Stdout, out _cpus, out _memTotal);
      }
    }

    bool anyRunning = false;
    for (int i = 0; i < containers.Count; i++)
    {
      anyRunning |= containers[i].IsRunning;
    }
    if (anyRunning)
    {
      CliResult stats = await _cli.RunAsync(Name, ["stats", "--no-stream", "--format", Formats.Stats], StatsTimeout, ct);
      if (stats.Ok)
      {
        Parsing.ApplyStats(stats.Stdout, containers);
      }
      await RefreshGpuAsync(containers, ct);
    }

    Publish(containers, images, null);
    return true;
  }

  /// <summary>Keeps last known usage numbers (so they don't flash to zero) and the GPU check result.</summary>
  static void CarryOver(List<Container> previous, List<Container> current)
  {
    Span<Container> cur = CollectionsMarshal.AsSpan(current);
    for (int i = 0; i < cur.Length; i++)
    {
      for (int j = 0; j < previous.Count; j++)
      {
        Container p = previous[j];
        if (p.Id != cur[i].Id)
        {
          continue;
        }
        cur[i].GpuChecked = p.GpuChecked;
        cur[i].WantsGpu = p.WantsGpu;
        if (cur[i].IsRunning)
        {
          cur[i].Cpu = p.Cpu;
          cur[i].MemBytes = p.MemBytes;
          cur[i].Gpu = p.Gpu;
        }
        break;
      }
    }
  }

  /// <summary>
  /// GPU usage goes through the CLI too: containers that requested devices get <c>nvidia-smi</c> run inside them.
  /// That reports utilization of the GPU(s) the container sees, which works for local and remote contexts alike.
  /// </summary>
  async Task RefreshGpuAsync(List<Container> containers, CancellationToken ct)
  {
    int toCheck = 0;
    for (int i = 0; i < containers.Count; i++)
    {
      if (!containers[i].GpuChecked)
      {
        toCheck++;
      }
    }
    if (toCheck > 0)
    {
      string[] inspect = new string[3 + toCheck];
      inspect[0] = "inspect";
      inspect[1] = "--format";
      inspect[2] = Formats.DeviceRequests;
      int n = 3;
      for (int i = 0; i < containers.Count; i++)
      {
        if (!containers[i].GpuChecked)
        {
          inspect[n++] = containers[i].Id;
        }
      }
      CliResult r = await _cli.RunAsync(Name, inspect, QuickTimeout, ct);
      Parsing.ApplyDeviceRequests(r.Stdout, containers);
    }

    for (int i = 0; i < containers.Count; i++)
    {
      Container c = containers[i];
      if (!c.WantsGpu || c.State != ContainerState.Running)
      {
        continue;
      }
      CliResult r = await _cli.RunAsync(Name, ["exec", c.Id, "nvidia-smi", "--query-gpu=utilization.gpu", "--format=csv,noheader,nounits"], GpuTimeout, ct);
      c.Gpu = r.Ok ? Parsing.NvidiaUtilization(r.Stdout) : -1;
      // No nvidia-smi in there: stop asking every refresh.
      c.WantsGpu = r.Ok;
      containers[i] = c;
    }
  }

  void Publish(List<Container> containers, List<Image> images, string? error)
  {
    _snapshot = new ContextSnapshot(Name, containers, images, _cpus, _memTotal, error, true);
  }

  static int CompareByProjectThenName(Container a, Container b)
  {
    // Standalone containers (no project) sort after projects; it doesn't matter to the UI, it only needs runs.
    int p = string.CompareOrdinal(a.Project ?? "￿", b.Project ?? "￿");
    return p != 0 ? p : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
  }
}
