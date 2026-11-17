using System;
using System.Numerics;

namespace PrimordialPower.Mathematics.Vectors;

public interface IVectorHasCross<TSelf, TResult>
where TSelf : IVectorHasCross<TSelf, TResult>
where TResult : IVector<TResult>
{
  TResult Cross(in TSelf other);
  static abstract TResult operator ^(in TSelf left, in TSelf right);
}
