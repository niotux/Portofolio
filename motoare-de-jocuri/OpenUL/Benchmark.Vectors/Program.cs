
using System.Numerics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using OpenUL.Mathematics.Vectors;

BenchmarkRunner.Run<VecBenchmark<Vec3F, float>>();
BenchmarkRunner.Run<VecBenchmark<Vec4F, float>>();
BenchmarkRunner.Run<VecBenchmark<Vec3_4D, double>>();
BenchmarkRunner.Run<VecBenchmark<Vec3D, double>>();
BenchmarkRunner.Run<VecBenchmark<Vec3DPtr, double>>();

[DisassemblyDiagnoser(maxDepth: 0, printSource: true, exportHtml: true)]
public class Vec3DBenchmark
{
  public Vec3DPtr vec2p = new(10, 20, 30);
  public Vec3DPtr vec1p = new(-10, 2, -5);
  public Vec3D vec2 = new(10, 20, 30);
  public Vec3D vec1 = new(-10, 2, -5);

  [Benchmark]
  public Vec3DPtr CrossPtr()
  {
    return Vec.Cross(vec1p, vec2p);
  }

  [Benchmark]
  public Vec3D Cross()
  {
    return Vec.Cross(vec1, vec2);
  }

  [Benchmark]
  public Vec3D Add() => vec1 + vec2;

  [Benchmark]
  public Vec3D Subtract() => vec1 - vec2;

  [Benchmark]
  public double Dot() => Vec<double>.Dot(vec1, vec2);


  [Benchmark]
  public Vec3DPtr AddPtr() => vec1p + vec2p;
  [Benchmark]
  public Vec3DPtr SubtractPtr() => vec1p - vec2p;

  [Benchmark]
  public double DotPtr() => Vec<double>.Dot(vec1p, vec2p);
}


[DisassemblyDiagnoser(maxDepth: 0, printSource: true, exportHtml: true)]
public class VecBenchmark<T, TElem>
  where T : IVector<T, TElem>, IVectorCross<T, T>
  where TElem : INumber<TElem>, IRootFunctions<TElem>
{
  public T vec2 = T.Create(TElem.One);
  public T vec1 = T.Create(TElem.One);

  [Benchmark]
  public T Cross()
  {
    return Vec.Cross(vec1, vec2);
  }

  [Benchmark]
  public T Add() => vec1 + vec2;

  [Benchmark]
  public T Subtract() => vec1 - vec2;

  [Benchmark]
  public TElem Dot() => Vec<TElem>.Dot(vec1, vec2);

  [Benchmark]
  public TElem Length()
  {
    return vec1.Length<TElem>();
  }

  [Benchmark]
  public TElem LengthSquared()
  {
    return vec1.LengthSquared<TElem>();
  }
}