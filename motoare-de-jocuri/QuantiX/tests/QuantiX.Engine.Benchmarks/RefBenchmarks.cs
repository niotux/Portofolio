using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;

using QuantiX.Engine.Native.Memory;
using QuantiX.Engine.Native.Utility;

namespace QuantiX.Engine.Benchmarks;

// ---------- unmanaged data types ----------

[StructLayout(LayoutKind.Sequential)]
public struct Transform
{
  public float PosX, PosY, PosZ;
  public float RotX, RotY, RotZ, RotW;
  public float ScaleX, ScaleY, ScaleZ;
}

[StructLayout(LayoutKind.Sequential)]
public struct Material
{
  public float R, G, B, A;
  public float Metallic, Roughness;
  public int TextureId;
  public int ShaderId;
}

[StructLayout(LayoutKind.Sequential)]
public struct MeshData
{
  public int VertexCount;
  public int IndexCount;
  public int MaterialSlot;
  public float BoundsMinX, BoundsMinY, BoundsMinZ;
  public float BoundsMaxX, BoundsMaxY, BoundsMaxZ;
}

// ---------- GC-based equivalents ----------

public class TransformBox
{
  public float PosX, PosY, PosZ;
  public float RotX, RotY, RotZ, RotW;
  public float ScaleX, ScaleY, ScaleZ;
}

public class MaterialBox
{
  public float R, G, B, A;
  public float Metallic, Roughness;
  public int TextureId;
  public int ShaderId;
}

public class MeshDataBox
{
  public int VertexCount;
  public int IndexCount;
  public int MaterialSlot;
  public float BoundsMinX, BoundsMinY, BoundsMinZ;
  public float BoundsMaxX, BoundsMaxY, BoundsMaxZ;
}

// ---------- StrongBox equivalents ----------

// StrongBox<T> is System.Runtime.CompilerServices.StrongBox<T>
// It's a class with a single public field: public T Value;

// ==========================================================================
//  BENCHMARKS
// ==========================================================================

[MemoryDiagnoser]
[ShortRunJob]
public class RefBenchmarks
{
  [Params(1000, 10_000)]
  public int N;

  // -----------------------------------------------------------------------
  //  1. Allocation + deallocation throughput
  // -----------------------------------------------------------------------

  [Benchmark(Baseline = true)]
  public void Class_AllocDealloc()
  {
    for (int i = 0; i < N; i++)
    {
      var t = new TransformBox();
      t.PosX = i;
    }
  }

  [Benchmark]
  public void StrongBox_AllocDealloc()
  {
    for (int i = 0; i < N; i++)
    {
      var t = new StrongBox<Transform>();
      t.Value.PosX = i;
    }
  }

  [Benchmark]
  public void Ref_AllocDealloc()
  {
    for (int i = 0; i < N; i++)
    {
      var t = Ref<Transform>.New();
      t.Value.PosX = i;
      t.Dispose();
    }
  }

  // -----------------------------------------------------------------------
  //  2. Pass (shared ownership) throughput
  // -----------------------------------------------------------------------

  [Benchmark]
  public void Class_PassAround()
  {
    var root = new TransformBox { PosX = 1f };
    var holders = new TransformBox[N];
    for (int i = 0; i < N; i++)
      holders[i] = root; // trivial reference copy
  }

  [Benchmark]
  public void StrongBox_PassAround()
  {
    var root = new StrongBox<Transform>(new Transform { PosX = 1f });
    var holders = new StrongBox<Transform>[N];
    for (int i = 0; i < N; i++)
      holders[i] = root; // trivial reference copy
  }

  [Benchmark]
  public void Ref_PassAround()
  {
    var root = Ref<Transform>.Box(new Transform { PosX = 1f });
    var holders = new Ref<Transform>[N];
    for (int i = 0; i < N; i++)
      holders[i] = root.Pass();
    for (int i = 0; i < N; i++)
      holders[i].Dispose();
    root.Dispose();
  }

  // -----------------------------------------------------------------------
  //  3. Clone (deep copy) throughput
  // -----------------------------------------------------------------------

  [Benchmark]
  public void Class_Clone()
  {
    var src = new TransformBox { PosX = 1f, ScaleX = 1f };
    for (int i = 0; i < N; i++)
    {
      var copy = new TransformBox
      {
        PosX = src.PosX, PosY = src.PosY, PosZ = src.PosZ,
        RotX = src.RotX, RotY = src.RotY, RotZ = src.RotZ, RotW = src.RotW,
        ScaleX = src.ScaleX, ScaleY = src.ScaleY, ScaleZ = src.ScaleZ,
      };
      _ = copy;
    }
  }

  [Benchmark]
  public void Ref_Clone()
  {
    var src = Ref<Transform>.Box(new Transform { PosX = 1f, ScaleX = 1f });
    for (int i = 0; i < N; i++)
    {
      var copy = src.Clone();
      copy.Dispose();
    }
    src.Dispose();
  }

  // -----------------------------------------------------------------------
  //  4. DAG dependency tree — scene graph style
  //     Scene → N nodes, every 4 nodes share a Material, every 8 share a Mesh
  //     No circular refs.
  // -----------------------------------------------------------------------

  [Benchmark]
  public void Class_DependencyTree()
  {
    var materials = new MaterialBox[N / 4 + 1];
    var meshes = new MeshDataBox[N / 8 + 1];

    for (int i = 0; i < materials.Length; i++)
      materials[i] = new MaterialBox { R = 1f, TextureId = i };
    for (int i = 0; i < meshes.Length; i++)
      meshes[i] = new MeshDataBox { VertexCount = 100 + i };

    var nodes = new (TransformBox Tr, MaterialBox Mat, MeshDataBox Mesh)[N];
    for (int i = 0; i < N; i++)
    {
      nodes[i] = (
        new TransformBox { PosX = i },
        materials[i / 4],
        meshes[i / 8]
      );
    }
  }

  [Benchmark]
  public void StrongBox_DependencyTree()
  {
    var materials = new StrongBox<Material>[N / 4 + 1];
    var meshes = new StrongBox<MeshData>[N / 8 + 1];

    for (int i = 0; i < materials.Length; i++)
      materials[i] = new StrongBox<Material>(new Material { R = 1f, TextureId = i });
    for (int i = 0; i < meshes.Length; i++)
      meshes[i] = new StrongBox<MeshData>(new MeshData { VertexCount = 100 + i });

    var nodes = new (StrongBox<Transform> Tr, StrongBox<Material> Mat, StrongBox<MeshData> Mesh)[N];
    for (int i = 0; i < N; i++)
    {
      nodes[i] = (
        new StrongBox<Transform>(new Transform { PosX = i }),
        materials[i / 4],
        meshes[i / 8]
      );
    }
  }

  [Benchmark]
  public void Ref_DependencyTree()
  {
    var materials = new Ref<Material>[N / 4 + 1];
    var meshes = new Ref<MeshData>[N / 8 + 1];

    for (int i = 0; i < materials.Length; i++)
      materials[i] = Ref<Material>.Box(new Material { R = 1f, TextureId = i });
    for (int i = 0; i < meshes.Length; i++)
      meshes[i] = Ref<MeshData>.Box(new MeshData { VertexCount = 100 + i });

    var nodes = new (Ref<Transform> Tr, Ref<Material> Mat, Ref<MeshData> Mesh)[N];
    for (int i = 0; i < N; i++)
    {
      nodes[i] = (
        Ref<Transform>.Box(new Transform { PosX = i }),
        materials[i / 4].Pass(),
        meshes[i / 8].Pass()
      );
    }

    // teardown — each node disposes its own refs
    for (int i = 0; i < N; i++)
    {
      nodes[i].Tr.Dispose();
      nodes[i].Mat.Dispose();
      nodes[i].Mesh.Dispose();
    }
    for (int i = 0; i < materials.Length; i++)
      materials[i].Dispose();
    for (int i = 0; i < meshes.Length; i++)
      meshes[i].Dispose();
  }

  // -----------------------------------------------------------------------
  //  5. RefArray vs managed array
  // -----------------------------------------------------------------------

  [Benchmark]
  public void ManagedArray_FillRead()
  {
    var arr = new Transform[N];
    for (int i = 0; i < N; i++)
      arr[i] = new Transform { PosX = i, PosY = i * 2f };
    float sum = 0;
    for (int i = 0; i < N; i++)
      sum += arr[i].PosX + arr[i].PosY;
  }

  [Benchmark]
  public void RefArray_FillRead()
  {
    var arr = new RefArray<Transform>(N);
    for (int i = 0; i < N; i++)
      arr[i] = new Transform { PosX = i, PosY = i * 2f };
    float sum = 0;
    for (int i = 0; i < N; i++)
      sum += arr[i].PosX + arr[i].PosY;
    arr.Dispose();
  }
}

// ==========================================================================
//  MULTITHREADED BENCHMARKS
// ==========================================================================

[MemoryDiagnoser]
[ShortRunJob]
public class RefMultithreadedBenchmarks
{
  [Params(4, 8)]
  public int ThreadCount;

  private const int OpsPerThread = 5_000;

  // -----------------------------------------------------------------------
  //  1. Concurrent Pass/Dispose — ref count contention
  // -----------------------------------------------------------------------

  [Benchmark(Baseline = true)]
  public void Class_ConcurrentShare()
  {
    var root = new TransformBox { PosX = 42f };
    var barrier = new Barrier(ThreadCount);
    var threads = new Thread[ThreadCount];

    for (int t = 0; t < ThreadCount; t++)
    {
      threads[t] = new Thread(() =>
      {
        barrier.SignalAndWait();
        for (int i = 0; i < OpsPerThread; i++)
        {
          var local = root; // no ref counting
          local.PosX += 1f;
          _ = local;
        }
      });
      threads[t].Start();
    }

    for (int t = 0; t < ThreadCount; t++)
      threads[t].Join();
  }

  [Benchmark]
  public void StrongBox_ConcurrentShare()
  {
    var root = new StrongBox<Transform>(new Transform { PosX = 42f });
    var barrier = new Barrier(ThreadCount);
    var threads = new Thread[ThreadCount];

    for (int t = 0; t < ThreadCount; t++)
    {
      threads[t] = new Thread(() =>
      {
        barrier.SignalAndWait();
        for (int i = 0; i < OpsPerThread; i++)
        {
          var local = root;
          local.Value.PosX += 1f;
          _ = local;
        }
      });
      threads[t].Start();
    }

    for (int t = 0; t < ThreadCount; t++)
      threads[t].Join();
  }

  [Benchmark]
  public void Ref_ConcurrentPassDispose()
  {
    var root = Ref<Transform>.Box(new Transform { PosX = 42f });
    var barrier = new Barrier(ThreadCount);
    var threads = new Thread[ThreadCount];

    for (int t = 0; t < ThreadCount; t++)
    {
      threads[t] = new Thread(() =>
      {
        barrier.SignalAndWait();
        for (int i = 0; i < OpsPerThread; i++)
        {
          var local = root.Pass();
          local.Value.PosX += 1f;
          local.Dispose();
        }
      });
      threads[t].Start();
    }

    for (int t = 0; t < ThreadCount; t++)
      threads[t].Join();

    root.Dispose();
  }

  // -----------------------------------------------------------------------
  //  2. Concurrent DAG — multiple threads build & tear down shared deps
  // -----------------------------------------------------------------------

  [Benchmark]
  public void Class_ConcurrentDAG()
  {
    // Shared resources created upfront
    var sharedMaterials = new MaterialBox[16];
    for (int i = 0; i < 16; i++)
      sharedMaterials[i] = new MaterialBox { R = i, TextureId = i };

    var barrier = new Barrier(ThreadCount);
    var threads = new Thread[ThreadCount];

    for (int t = 0; t < ThreadCount; t++)
    {
      threads[t] = new Thread(() =>
      {
        barrier.SignalAndWait();
        var localNodes = new (TransformBox Tr, MaterialBox Mat)[OpsPerThread];
        for (int i = 0; i < OpsPerThread; i++)
        {
          localNodes[i] = (
            new TransformBox { PosX = i },
            sharedMaterials[i % 16]
          );
        }
      });
      threads[t].Start();
    }

    for (int t = 0; t < ThreadCount; t++)
      threads[t].Join();
  }

  [Benchmark]
  public void Ref_ConcurrentDAG()
  {
    // Shared resources created upfront
    var sharedMaterials = new Ref<Material>[16];
    for (int i = 0; i < 16; i++)
      sharedMaterials[i] = Ref<Material>.Box(new Material { R = i, TextureId = i });

    var barrier = new Barrier(ThreadCount);
    var threads = new Thread[ThreadCount];

    for (int t = 0; t < ThreadCount; t++)
    {
      threads[t] = new Thread(() =>
      {
        barrier.SignalAndWait();
        var localNodes = new (Ref<Transform> Tr, Ref<Material> Mat)[OpsPerThread];
        for (int i = 0; i < OpsPerThread; i++)
        {
          localNodes[i] = (
            Ref<Transform>.Box(new Transform { PosX = i }),
            sharedMaterials[i % 16].Pass()
          );
        }
        // teardown
        for (int i = 0; i < OpsPerThread; i++)
        {
          localNodes[i].Tr.Dispose();
          localNodes[i].Mat.Dispose();
        }
      });
      threads[t].Start();
    }

    for (int t = 0; t < ThreadCount; t++)
      threads[t].Join();

    for (int i = 0; i < 16; i++)
      sharedMaterials[i].Dispose();
  }

  // -----------------------------------------------------------------------
  //  3. Producer-consumer — one thread creates, N threads consume & dispose
  // -----------------------------------------------------------------------

  [Benchmark]
  public void Class_ProducerConsumer()
  {
    var queue = new System.Collections.Concurrent.ConcurrentQueue<TransformBox>();
    var done = new ManualResetEventSlim(false);
    var barrier = new Barrier(ThreadCount + 1);

    // producer
    var producer = new Thread(() =>
    {
      barrier.SignalAndWait();
      for (int i = 0; i < OpsPerThread * ThreadCount; i++)
        queue.Enqueue(new TransformBox { PosX = i });
      done.Set();
    });
    producer.Start();

    var consumers = new Thread[ThreadCount];
    for (int t = 0; t < ThreadCount; t++)
    {
      consumers[t] = new Thread(() =>
      {
        barrier.SignalAndWait();
        while (!done.IsSet || !queue.IsEmpty)
        {
          if (queue.TryDequeue(out var item))
            _ = item.PosX;
        }
      });
      consumers[t].Start();
    }

    producer.Join();
    for (int t = 0; t < ThreadCount; t++)
      consumers[t].Join();
    done.Dispose();
  }

  [Benchmark]
  public void Ref_ProducerConsumer()
  {
    var queue = new System.Collections.Concurrent.ConcurrentQueue<Ref<Transform>>();
    var done = new ManualResetEventSlim(false);
    var barrier = new Barrier(ThreadCount + 1);

    // producer
    var producer = new Thread(() =>
    {
      barrier.SignalAndWait();
      for (int i = 0; i < OpsPerThread * ThreadCount; i++)
        queue.Enqueue(Ref<Transform>.Box(new Transform { PosX = i }));
      done.Set();
    });
    producer.Start();

    var consumers = new Thread[ThreadCount];
    for (int t = 0; t < ThreadCount; t++)
    {
      consumers[t] = new Thread(() =>
      {
        barrier.SignalAndWait();
        while (!done.IsSet || !queue.IsEmpty)
        {
          if (queue.TryDequeue(out var item))
          {
            _ = item.Value.PosX;
            item.Dispose();
          }
        }
      });
      consumers[t].Start();
    }

    producer.Join();
    for (int t = 0; t < ThreadCount; t++)
      consumers[t].Join();
    done.Dispose();
  }
}
