using System.Numerics;

namespace PrimordialPower.Mathematics.Matrices;

public interface IMatrixTransposable<TSelf, TTransposed> :
  IMatrix<TSelf>
where TSelf : IMatrixTransposable<TSelf, TTransposed>
where TTransposed : IMatrixTransposable<TTransposed, TSelf>
{
  TTransposed Transposed { get; }
}