using System;
using System.Numerics;

namespace PrimordialPower.Mathematics.Vectors;

public interface IVectorFloating<TSelf> :
  IVector<TSelf>
where TSelf : IVectorFloating<TSelf>
{
  TSelf Normalized { get; }
  void Normalize();

  static abstract TSelf operator ~(in TSelf right);
}

public interface IVectorFloating<TSelf, TValue> :
  IVectorFloating<TSelf>,
  IVector<TSelf, TValue>
where TSelf : IVectorFloating<TSelf, TValue>
where TValue : INumber<TValue>
{
  TValue Length { get; set; }
  TValue Distance(in TSelf vector);
}
