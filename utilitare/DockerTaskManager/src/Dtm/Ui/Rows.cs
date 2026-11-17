using System;
using System.Collections.Generic;
using Dtm.Docker;

namespace Dtm.Ui;

public enum RowKind
{
  Project,
  Container,
  Image,
  ContextError
}

public enum SortColumn
{
  Name,
  Context,
  Status,
  Cpu,
  Mem,
  Gpu
}

/// <summary>Identifies a row across refreshes. Id is the project name, container ID or image ID.</summary>
public readonly record struct RowKey
(
  RowKind Kind,
  string Context,
  string Id
);

/// <summary>One visible line of the list. Points into its snapshot rather than copying data out of it.</summary>
public struct Row
{
  public RowKind Kind;
  public ContextPoller Poller;
  public ContextSnapshot Snapshot;
  /// <summary>Index into Snapshot.Containers (project: its first container) or Snapshot.Images.</summary>
  public int First;
  /// <summary>Number of containers in a project; 1 otherwise.</summary>
  public int Count;
  public bool Child;
  public int Running;
  public double Cpu;
  public long Mem;
  public double Gpu;

  public readonly Container Container
  {
    get { return Snapshot.Containers[First]; }
  }

  public readonly Image Image
  {
    get { return Snapshot.Images[First]; }
  }

  public readonly string Name
  {
    get
    {
      switch (Kind)
      {
        case RowKind.Project:
          return Snapshot.Containers[First].Project!;
        case RowKind.Container:
          return Snapshot.Containers[First].Name;
        case RowKind.Image:
          return Snapshot.Images[First].Repository;
        default:
          return Snapshot.Name;
      }
    }
  }

  public readonly RowKey Key
  {
    get
    {
      switch (Kind)
      {
        case RowKind.Project:
          return new RowKey(Kind, Snapshot.Name, Snapshot.Containers[First].Project!);
        case RowKind.Container:
          return new RowKey(Kind, Snapshot.Name, Snapshot.Containers[First].Id);
        case RowKind.Image:
          return new RowKey(Kind, Snapshot.Name, Snapshot.Images[First].Id);
        default:
          return new RowKey(Kind, Snapshot.Name, "");
      }
    }
  }
}

/// <summary>Sort order for top-level rows. One reused instance; the view sets its fields.</summary>
public sealed class RowComparer : IComparer<Row>
{
  public SortColumn Column;
  public bool Descending;

  public int Compare(Row a, Row b)
  {
    int c;
    switch (Column)
    {
      case SortColumn.Context:
        c = string.CompareOrdinal(a.Snapshot.Name, b.Snapshot.Name);
        break;
      case SortColumn.Status:
        c = b.Running.CompareTo(a.Running);
        break;
      case SortColumn.Cpu:
        c = b.Cpu.CompareTo(a.Cpu);
        break;
      case SortColumn.Mem:
        c = b.Mem.CompareTo(a.Mem);
        break;
      case SortColumn.Gpu:
        c = b.Gpu.CompareTo(a.Gpu);
        break;
      default:
        c = 0;
        break;
    }
    if (c == 0)
    {
      c = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
    }
    if (c == 0 && a.Kind == RowKind.Image)
    {
      c = string.CompareOrdinal(a.Image.Tag, b.Image.Tag);
    }
    if (c == 0)
    {
      c = string.CompareOrdinal(a.Snapshot.Name, b.Snapshot.Name);
    }
    return Descending ? -c : c;
  }
}

/// <summary>Turns snapshots into the flat list of visible rows.</summary>
public static class RowBuilder
{
  /// <param name="top">Scratch list for top-level rows; cleared and reused.</param>
  public static void Containers(ContextPoller[] pollers, ReadOnlySpan<char> filter, bool runningOnly, List<RowKey> collapsed, RowComparer comparer, List<Row> top, List<Row> into)
  {
    top.Clear();
    into.Clear();
    for (int p = 0; p < pollers.Length; p++)
    {
      ContextSnapshot s = pollers[p].Snapshot;
      bool contextMatches = Matches(s.Name, filter);
      List<Container> cs = s.Containers;
      int i = 0;
      while (i < cs.Count)
      {
        int end = i + 1;
        string? project = cs[i].Project;
        if (project != null)
        {
          while (end < cs.Count && cs[end].Project == project)
          {
            end++;
          }
        }

        Row row = new();
        row.Poller = pollers[p];
        row.Snapshot = s;
        row.First = i;
        row.Count = end - i;
        row.Kind = project != null ? RowKind.Project : RowKind.Container;
        row.Gpu = -1;
        bool projectMatches = project != null && (contextMatches || Matches(project, filter));
        int visible = 0;
        for (int k = i; k < end; k++)
        {
          Container c = cs[k];
          if (c.IsRunning)
          {
            row.Running++;
            row.Cpu += c.Cpu;
            row.Mem += c.MemBytes;
            if (c.Gpu > row.Gpu)
            {
              row.Gpu = c.Gpu;
            }
          }
          if (ChildVisible(c, projectMatches || contextMatches, filter, runningOnly))
          {
            visible++;
          }
        }
        if (visible > 0)
        {
          top.Add(row);
        }
        i = end;
      }
    }

    top.Sort(comparer);

    for (int t = 0; t < top.Count; t++)
    {
      Row row = top[t];
      into.Add(row);
      if (row.Kind != RowKind.Project || collapsed.Contains(row.Key))
      {
        continue;
      }
      List<Container> cs = row.Snapshot.Containers;
      bool parentMatches = Matches(row.Snapshot.Name, filter) || Matches(cs[row.First].Project, filter);
      for (int k = row.First; k < row.First + row.Count; k++)
      {
        Container c = cs[k];
        if (!ChildVisible(c, parentMatches, filter, runningOnly))
        {
          continue;
        }
        Row child = row;
        child.Kind = RowKind.Container;
        child.First = k;
        child.Count = 1;
        child.Child = true;
        child.Running = c.IsRunning ? 1 : 0;
        child.Cpu = c.Cpu;
        child.Mem = c.MemBytes;
        child.Gpu = c.Gpu;
        into.Add(child);
      }
    }

    // Unreachable contexts go last, so they're visible but out of the way.
    for (int p = 0; p < pollers.Length; p++)
    {
      ContextSnapshot s = pollers[p].Snapshot;
      if (s.Error != null && Matches(s.Name, filter))
      {
        Row row = new();
        row.Kind = RowKind.ContextError;
        row.Poller = pollers[p];
        row.Snapshot = s;
        row.Count = 1;
        into.Add(row);
      }
    }
  }

  public static void Images(ContextPoller[] pollers, ReadOnlySpan<char> filter, RowComparer comparer, List<Row> into)
  {
    into.Clear();
    for (int p = 0; p < pollers.Length; p++)
    {
      ContextSnapshot s = pollers[p].Snapshot;
      bool contextMatches = Matches(s.Name, filter);
      for (int i = 0; i < s.Images.Count; i++)
      {
        Image img = s.Images[i];
        if (!contextMatches && !Matches(img.Repository, filter) && !Matches(img.Tag, filter) && !Matches(img.Id, filter))
        {
          continue;
        }
        Row row = new();
        row.Kind = RowKind.Image;
        row.Poller = pollers[p];
        row.Snapshot = s;
        row.First = i;
        row.Count = 1;
        row.Gpu = -1;
        into.Add(row);
      }
    }
    into.Sort(comparer);
  }

  static bool ChildVisible(Container c, bool parentMatches, ReadOnlySpan<char> filter, bool runningOnly)
  {
    if (runningOnly && !c.IsRunning)
    {
      return false;
    }
    return parentMatches || Matches(c.Name, filter) || Matches(c.Image, filter);
  }

  public static bool Matches(string? s, ReadOnlySpan<char> filter)
  {
    if (filter.IsEmpty)
    {
      return true;
    }
    return s != null && s.AsSpan().Contains(filter, StringComparison.OrdinalIgnoreCase);
  }
}
