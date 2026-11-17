using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Dtm.Docker;

/// <summary>
/// All contexts Docker knows about, each with its own poller. The context list itself comes from
/// <c>docker context ls</c> and is re-read periodically, so added or removed contexts show up on their own.
/// </summary>
public sealed class Fleet
{
  static readonly TimeSpan ContextInterval = TimeSpan.FromSeconds(15);
  static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

  readonly DockerCli _cli;
  readonly CancellationTokenSource _stop = new();
  volatile ContextPoller[] _pollers = [];
  volatile string? _error;

  public Fleet(DockerCli cli)
  {
    _cli = cli;
  }

  /// <summary>Current pollers, sorted by name. The array is replaced, never mutated.</summary>
  public ContextPoller[] Pollers
  {
    get { return _pollers; }
  }

  /// <summary>Set when <c>docker context ls</c> itself fails (e.g. docker isn't installed).</summary>
  public string? Error
  {
    get { return _error; }
  }

  public void Start()
  {
    _ = Task.Run(LoopAsync);
  }

  public void Stop()
  {
    _stop.Cancel();
    ContextPoller[] pollers = _pollers;
    for (int i = 0; i < pollers.Length; i++)
    {
      pollers[i].Stop();
    }
  }

  async Task LoopAsync()
  {
    CancellationToken ct = _stop.Token;
    while (!ct.IsCancellationRequested)
    {
      CliResult r = await _cli.RunAsync(null, ["context", "ls", "--format", Formats.Contexts], Timeout, ct);
      if (r.Ok)
      {
        _error = null;
        List<DockerContext> contexts = new();
        Parsing.Contexts(r.Stdout, contexts);
        Sync(contexts);
      }
      else if (!ct.IsCancellationRequested)
      {
        _error = r.ErrorLine();
      }
      try
      {
        await Task.Delay(ContextInterval, ct);
      }
      catch (OperationCanceledException)
      {
        return;
      }
    }
  }

  /// <summary>Keeps pollers for contexts that still exist, starts new ones, stops removed ones.</summary>
  void Sync(List<DockerContext> contexts)
  {
    ContextPoller[] old = _pollers;
    ContextPoller[] next = new ContextPoller[contexts.Count];
    for (int i = 0; i < contexts.Count; i++)
    {
      string name = contexts[i].Name;
      ContextPoller? existing = null;
      for (int j = 0; j < old.Length; j++)
      {
        if (old[j].Name == name)
        {
          existing = old[j];
          break;
        }
      }
      if (existing == null)
      {
        existing = new ContextPoller(name, _cli);
        existing.Start();
      }
      next[i] = existing;
    }
    for (int j = 0; j < old.Length; j++)
    {
      if (Array.IndexOf(next, old[j]) < 0)
      {
        old[j].Stop();
      }
    }
    Array.Sort(next, ComparePollers);
    _pollers = next;
  }

  static int ComparePollers(ContextPoller a, ContextPoller b)
  {
    return string.CompareOrdinal(a.Name, b.Name);
  }
}
