using System;
using System.Numerics;

namespace PrimordialPower.Mathematics.Vectors;

public interface IVectorFloatingIeee754<TSelf> : IVectorFloating<TSelf>
where TSelf : IVectorFloatingIeee754<TSelf>
{
  TSelf NormalizedFast { get; }
  void NormalizeFast();
}

public interface IVectorFloatingIeee754<TSelf, TValueFloat> :
  IVectorFloating<TSelf, TValueFloat>,
  IVectorFloatingIeee754<TSelf>
where TSelf : IVectorFloatingIeee754<TSelf, TValueFloat>
where TValueFloat : IFloatingPointIeee754<TValueFloat>
{
  TValueFloat LengthFast { get; set; }
  TValueFloat DistanceFast(in TSelf vector);
}
