using System.Numerics;

namespace PrimordialPower.Mathematics.Matrices;

public interface IMatrixHasMatrixProduct<TSelf, TOther, TProduct> :
  IMatrix<TSelf>
where TSelf : IMatrixHasMatrixProduct<TSelf, TOther, TProduct>
where TOther : IMatrix<TOther>
where TProduct : IMatrix<TProduct>
{
  TProduct Multiply(in TOther other);
  static abstract TProduct operator *(in TSelf left, in TOther right);
}