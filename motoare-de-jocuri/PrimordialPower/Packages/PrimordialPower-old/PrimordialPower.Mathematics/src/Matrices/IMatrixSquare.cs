using System;
using System.Numerics;

namespace PrimordialPower.Mathematics.Matrices;

public interface IMatrixSquare<TSelf> :
  IMatrixTransposable<TSelf, TSelf>,
  IMatrixHasMatrixProduct<TSelf, TSelf, TSelf>
where TSelf : IMatrixSquare<TSelf>
{
  void Transpose();
}



public interface IMatrixSquare<TSelf, TValue> :
  IMatrixSquare<TSelf>,
  IMatrix<TSelf, TValue>
where TSelf : IMatrixSquare<TSelf, TValue>
where TValue : INumber<TValue>
{
  TValue Determinant { get; }
  TValue Trace { get; }
}
