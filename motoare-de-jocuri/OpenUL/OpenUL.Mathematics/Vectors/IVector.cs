using System.Numerics;

namespace OpenUL.Mathematics.Vectors;

public interface IVector<TSelf>
: IAdditionOperators<TSelf, TSelf, TSelf>,
  ISubtractionOperators<TSelf, TSelf, TSelf>,
  IMultiplyOperators<TSelf, TSelf, TSelf>,
  IDivisionOperators<TSelf, TSelf, TSelf>,
  IUnaryPlusOperators<TSelf, TSelf>,
  IUnaryNegationOperators<TSelf, TSelf>,
  IEquatable<TSelf>
  where TSelf : IVector<TSelf>
{
  static abstract T Dot<T>(TSelf left, TSelf right, NumberCreation creation = NumberCreation.Saturate)
    where T : INumber<T>;

  T LengthSquared<T>(NumberCreation creation = NumberCreation.Saturate)
    where T : INumber<T>;

  T Length<T>(NumberCreation creation = NumberCreation.Saturate)
    where T : INumber<T>, IRootFunctions<T>;
}

public interface IVector<TSelf, TElem> : IVector<TSelf>
  where TSelf : IVector<TSelf, TElem>
  where TElem : INumber<TElem>
{
  static abstract TSelf Create(TElem value);
  TElem this[int index] { get; set; }
}

public interface IVectorCross<TSelf, TResult> : IVector<TSelf>
  where TSelf : IVector<TSelf>
{
  static abstract TResult Cross(TSelf left, TSelf right);
}

