using System.Numerics;

namespace QuantiX.Engine.Mathematics.Vectors;

public interface IVecXComponent<T>
{
  public T X { get; set; }
}

public interface IVecYComponent<T>
{
  public T Y { get; set; }
}

public interface IVecZComponent<T>
{
  public T Z { get; set; }
}

public interface IVecWComponent<T>
{
  public T W { get; set; }
}

public interface IVector<TSelf> :
  IMultiplyOperators<TSelf, TSelf, TSelf>,
  ISubtractionOperators<TSelf, TSelf, TSelf>,
  IAdditionOperators<TSelf, TSelf, TSelf>,
  IDivisionOperators<TSelf, TSelf, TSelf>,
  IEqualityOperators<TSelf, TSelf, bool>,
  IEquatable<TSelf>
  where TSelf : IVector<TSelf>
{
  static abstract TSelf Identity { get; }
  static abstract TSelf Zero { get; }
  static abstract int Dimensions { get; }

  static abstract TSelf Add(in TSelf left, in TSelf right);
  static abstract TSelf Subtract(in TSelf left, in TSelf right);
  static abstract TSelf Multiply(in TSelf left, in TSelf right);
  static abstract TSelf Divide(in TSelf left, in TSelf right);
}

public interface IVectorOfScalar<TSelf, TScalar> :
  IMultiplyOperators<TSelf, TScalar, TSelf>,
  ISubtractionOperators<TSelf, TScalar, TSelf>,
  IAdditionOperators<TSelf, TScalar, TSelf>,
  IDivisionOperators<TSelf, TScalar, TSelf>
  where TSelf : IVectorOfScalar<TSelf, TScalar>, IVector<TSelf>
  where TScalar :
    IAdditionOperators<TScalar, TScalar, TScalar>,
    ISubtractionOperators<TScalar, TScalar, TScalar>,
    IMultiplyOperators<TScalar, TScalar, TScalar>,
    IDivisionOperators<TScalar, TScalar, TScalar>
{
  static abstract TSelf Add(in TSelf left, TScalar right);
  static abstract TSelf Subtract(in TSelf left, TScalar right);
  static abstract TSelf Multiply(in TSelf left, TScalar right);
  static abstract TSelf Divide(in TSelf left, TScalar right);
  static abstract TScalar Dot(in TSelf left, in TSelf right);
  static abstract TScalar DistanceSquared(in TSelf vec1, in TSelf vec2);

  TScalar LengthSquared { get; }
}

public interface IVectorWithCrossProduct<TSelf, TResult>
  where TSelf : IVectorWithCrossProduct<TSelf, TResult>, IVector<TSelf>
  where TResult : IVector<TResult>
{
  static abstract TResult Cross(in TSelf left, in TSelf right);
}

public interface IVectorOfSqrtScalar<TSelf, TScalar>
  where TSelf : IVectorOfSqrtScalar<TSelf, TScalar>, IVectorOfScalar<TSelf, TScalar>, IVector<TSelf>
  where TScalar :
    IAdditionOperators<TScalar, TScalar, TScalar>,
    ISubtractionOperators<TScalar, TScalar, TScalar>,
    IMultiplyOperators<TScalar, TScalar, TScalar>,
    IDivisionOperators<TScalar, TScalar, TScalar>,
    IRootFunctions<TScalar>
{
  TScalar Length { get; }
  TSelf Normalized { get; }
  TSelf Normalize();
  static abstract TScalar Distance(in TSelf a, in TSelf b);
}
