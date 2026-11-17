using System;
using System.Numerics;

namespace PrimordialPower.Mathematics.Vectors;

public interface IVector<TSelf>
where TSelf : IVector<TSelf>
{
  uint Dimensions { get; }
  static abstract uint DimCount { get; }


  static abstract TSelf Identity { get; }
  static abstract TSelf Zero { get; }

  TSelf Neg();
  TSelf Add(in TSelf other);
  TSelf Subtract(in TSelf other);
  TSelf Multiply(in TSelf other);
  TSelf Divide(in TSelf other);
  TSelf Modulo(in TSelf other);

  static abstract TSelf operator -(in TSelf right);
  static abstract TSelf operator +(in TSelf left, in TSelf right);
  static abstract TSelf operator -(in TSelf left, in TSelf right);
  static abstract TSelf operator *(in TSelf left, in TSelf right);
  static abstract TSelf operator /(in TSelf left, in TSelf right);
  static abstract TSelf operator %(in TSelf left, in TSelf right);
}



public interface IVector<TSelf, TValue> : IVector<TSelf>
where TSelf : IVector<TSelf, TValue>
where TValue : INumber<TValue>
{
  TValue LengthSquared { get; }
  TValue DistanceSquared(in TSelf vector);



  TSelf Add(TValue scalar);
  TSelf Subtract(TValue scalar);
  TSelf Multiply(TValue scalar);
  TSelf Divide(TValue scalar);
  TSelf Modulo(TValue other);
  TValue Dot(in TSelf other);




  static abstract TSelf operator +(in TSelf left, TValue right);
  static abstract TSelf operator +(TValue left, in TSelf right);

  static abstract TSelf operator -(in TSelf left, TValue right);

  static abstract TSelf operator *(in TSelf left, TValue right);
  static abstract TSelf operator *(TValue left, in TSelf right);

  static abstract TSelf operator /(in TSelf left, TValue right);

  static abstract TSelf operator %(in TSelf left, in TSelf right);



  TValue this[int index] { get; set; }
  TValue this[uint index] { get; set; }
}
