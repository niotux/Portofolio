using System.Numerics;

namespace PrimordialPower.Mathematics.Matrices;

public interface IMatrix<TSelf>
where TSelf : IMatrix<TSelf>
{
  uint Rows { get; }
  static abstract uint RowCount { get; }
  uint Columns { get; }
  static abstract uint ColumnCount { get; }
  uint Elements { get; }
  static abstract uint ElementCount { get; }

  static abstract TSelf Identity { get; }
  static abstract TSelf Zero { get; }



  TSelf Neg();
  TSelf Add(in TSelf other);
  TSelf Subtract(in TSelf other);
  TSelf Modulo(in TSelf other);
  TSelf CompMultiply(in TSelf other);
  TSelf CompDivide(in TSelf other);



  static abstract TSelf operator -(in TSelf right);
  static abstract TSelf operator +(in TSelf left, in TSelf right);
  static abstract TSelf operator -(in TSelf left, in TSelf right);
  static abstract TSelf operator %(in TSelf left, in TSelf right);
}



public interface IMatrix<TSelf, TValue> : IMatrix<TSelf>
where TSelf : IMatrix<TSelf, TValue>
where TValue : INumber<TValue>
{
  TValue this[int row, int col] { get; set; }
  TValue this[uint row, uint col] { get; set; }



  TSelf Add(TValue scalar);
  TSelf Subtract(TValue scalar);
  TSelf CompMultiply(TValue scalar);
  TSelf CompDivide(TValue scalar);



  static abstract TSelf operator +(in TSelf left, TValue right);
  static abstract TSelf operator +(TValue left, in TSelf right);
  static abstract TSelf operator -(in TSelf left, TValue right);
  static abstract TSelf operator *(in TSelf left, TValue right);
  static abstract TSelf operator *(TValue left, in TSelf right);
  static abstract TSelf operator /(in TSelf left, TValue right);
}