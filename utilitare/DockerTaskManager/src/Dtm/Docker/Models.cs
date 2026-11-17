using System.Collections.Generic;

namespace Dtm.Docker;

public enum ContainerState
{
  Unknown,
  Created,
  Running,
  Paused,
  Restarting,
  Removing,
  Exited,
  Dead
}

public readonly record struct DockerContext
(
  string Name,
  bool Current,
  string? Error
);

public record struct Container
(
  string Id,
  string Name,
  string Image,
  ContainerState State,
  string Status,
  string? Project,
  double Cpu,
  long MemBytes,
  double Gpu,
  bool GpuChecked,
  bool WantsGpu
)
{
  public readonly bool IsRunning
  {
    get { return State == ContainerState.Running || State == ContainerState.Restarting || State == ContainerState.Paused; }
  }
}

public readonly record struct Image
(
  string Id,
  string Repository,
  string Tag,
  string Size,
  string CreatedSince
);

/// <summary>
/// Everything known about one context at one moment. Built by its poller, then published and never mutated.
/// Containers are sorted by project, then name, so a compose project is a contiguous run.
/// </summary>
public sealed class ContextSnapshot
{
  public readonly string Name;
  public readonly List<Container> Containers;
  public readonly List<Image> Images;
  public readonly int Cpus;
  public readonly long MemTotal;
  public readonly string? Error;
  public readonly bool Loaded;

  public ContextSnapshot(string name, List<Container> containers, List<Image> images, int cpus, long memTotal, string? error, bool loaded)
  {
    Name = name;
    Containers = containers;
    Images = images;
    Cpus = cpus;
    MemTotal = memTotal;
    Error = error;
    Loaded = loaded;
  }
}
