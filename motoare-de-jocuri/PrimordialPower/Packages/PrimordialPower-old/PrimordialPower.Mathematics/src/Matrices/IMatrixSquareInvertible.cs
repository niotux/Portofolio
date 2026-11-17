using System;
using System.Numerics;

namespace PrimordialPower.Mathematics.Matrices;

public interface IMatrixSquareInvertible<TSelf> :
  IMatrixSquare<TSelf>
where TSelf : IMatrixSquareInvertible<TSelf>
{
  bool TryGetInverted(out TSelf inverted);
  bool TryInvert();
}
