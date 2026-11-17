using System.Collections.Concurrent;
using QuantiX.Engine.Mathematics.Vectors;

namespace QuantiX.Engine.Tests.Rendering;

internal enum ChunkWorkState { Queued, Working, MeshReady, Uploaded }

internal sealed class ChunkEntry(Chunk chunk)
{
  public readonly Chunk Chunk = chunk;

  // Written by background thread, read by main thread via volatile + ConcurrentQueue barrier
  public volatile bool TerrainReady;
  public volatile ChunkWorkState WorkState = ChunkWorkState.Queued;
  public float[]? Verts;
  public uint[]? Inds;

  // Written by either thread, read by both
  public volatile bool Cancelled;
  public volatile bool DirtyMesh;
}

internal sealed class InfiniteWorld
{
  public const int ViewDistance = 50;
  private const int UnloadDistance = ViewDistance + 3;

  private readonly ConcurrentDictionary<(int, int), ChunkEntry> _entries = new();
  private readonly ConcurrentQueue<ChunkEntry> _readyQueue = new();
  private readonly ConcurrentQueue<ChunkEntry> _unloadQueue = new();

  public BlockType GetBlock(int x, int y, int z)
  {
    if (y < 0 || y >= Chunk.SizeY) return BlockType.Air;
    int cx = FloorDiv(x, Chunk.SizeX);
    int cz = FloorDiv(z, Chunk.SizeZ);
    if (!_entries.TryGetValue((cx, cz), out var e) || !e.TerrainReady) return BlockType.Air;
    return e.Chunk.GetLocal(x - cx * Chunk.SizeX, y, z - cz * Chunk.SizeZ);
  }

  public void SetBlock(int x, int y, int z, BlockType type)
  {
    int cx = FloorDiv(x, Chunk.SizeX);
    int cz = FloorDiv(z, Chunk.SizeZ);
    if (!_entries.TryGetValue((cx, cz), out var entry) || !entry.TerrainReady) return;
    entry.Chunk.SetLocal(x - cx * Chunk.SizeX, y, z - cz * Chunk.SizeZ, type);
    ScheduleRemesh(entry);
  }

  // Called from main thread each frame
  public void Update(int camChunkX, int camChunkZ)
  {
    for (int dz = -ViewDistance; dz <= ViewDistance; dz++)
      for (int dx = -ViewDistance; dx <= ViewDistance; dx++)
      {
        if (dx * dx + dz * dz > ViewDistance * ViewDistance) continue;
        var key = (camChunkX + dx, camChunkZ + dz);
        if (_entries.ContainsKey(key)) continue;
        var entry = new ChunkEntry(new Chunk(key.Item1, key.Item2));
        if (_entries.TryAdd(key, entry))
          ThreadPool.QueueUserWorkItem(_ => DoWork(entry));
      }

    foreach (var (key, entry) in _entries)
    {
      int dx = key.Item1 - camChunkX;
      int dz = key.Item2 - camChunkZ;
      if (dx * dx + dz * dz > UnloadDistance * UnloadDistance)
      {
        entry.Cancelled = true;
        if (_entries.TryRemove(key, out _))
          _unloadQueue.Enqueue(entry);
      }
    }
  }

  // Called from main thread after GPU upload to allow re-queue if block edits happened during meshing
  public void NotifyUploaded(ChunkEntry entry)
  {
    if (entry.DirtyMesh && !entry.Cancelled)
    {
      entry.WorkState = ChunkWorkState.Working;
      ThreadPool.QueueUserWorkItem(_ => DoWork(entry));
    }
    else
    {
      entry.WorkState = ChunkWorkState.Uploaded;
    }
  }

  public bool TryDequeueReady(out ChunkEntry entry) => _readyQueue.TryDequeue(out entry!);
  public bool TryDequeueUnload(out ChunkEntry entry) => _unloadQueue.TryDequeue(out entry!);

  public (int hx, int hy, int hz, int px, int py, int pz)? Raycast(Vec3<float> origin, Vec3<float> direction, float maxDist)
  {
    int prevX = int.MinValue, prevY = int.MinValue, prevZ = int.MinValue;
    const float step = 0.05f;
    for (float t = 0; t < maxDist; t += step)
    {
      var pos = origin + direction * t;
      int bx = (int)MathF.Floor(pos.X);
      int by = (int)MathF.Floor(pos.Y);
      int bz = (int)MathF.Floor(pos.Z);
      if (bx == prevX && by == prevY && bz == prevZ) continue;
      if (GetBlock(bx, by, bz) != BlockType.Air)
        return (bx, by, bz, prevX, prevY, prevZ);
      prevX = bx; prevY = by; prevZ = bz;
    }
    return null;
  }

  private void ScheduleRemesh(ChunkEntry entry)
  {
    entry.DirtyMesh = true;
    if (entry.WorkState == ChunkWorkState.Uploaded)
    {
      entry.WorkState = ChunkWorkState.Working;
      ThreadPool.QueueUserWorkItem(_ => DoWork(entry));
    }
    // If Working/MeshReady, DirtyMesh flag is caught in NotifyUploaded
  }

  private void DoWork(ChunkEntry entry)
  {
    entry.WorkState = ChunkWorkState.Working;

    if (!entry.TerrainReady)
    {
      entry.Chunk.Generate();
      entry.TerrainReady = true;
    }
    if (entry.Cancelled) return;

    entry.DirtyMesh = false;
    var (verts, inds) = entry.Chunk.BuildMesh(GetBlock);
    if (entry.Cancelled) return;

    entry.Verts = verts;
    entry.Inds = inds;
    entry.WorkState = ChunkWorkState.MeshReady;
    _readyQueue.Enqueue(entry);
  }

  private static int FloorDiv(int a, int b) => (int)MathF.Floor((float)a / b);
}
