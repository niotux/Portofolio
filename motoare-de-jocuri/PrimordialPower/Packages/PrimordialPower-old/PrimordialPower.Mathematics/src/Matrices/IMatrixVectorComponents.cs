using PrimordialPower.Mathematics.Vectors;

namespace PrimordialPower.Mathematics.Matrices;

public interface IMatrixVectorComponents<TSelf, TColumn, TRow> :
  IMatrix<TSelf>
where TSelf : IMatrixVectorComponents<TSelf, TColumn, TRow>
where TColumn : IVector<TColumn>
where TRow : IVector<TRow>
{
  TRow this[int row] { get; set; }
  TRow this[uint row] { get; set; }

  TRow GetRow(int row);
  TRow GetRow(uint row);
  TColumn GetColumn(int row);
  TColumn GetColumn(uint row);

  void SetRow(int row, in TRow vec);
  void SetRow(uint row, in TRow vec);
  void SetColumn(int row, in TColumn vec);
  void SetColumn(uint row, in TColumn vec);

  TColumn Multiply(in TRow vec);
  TRow LeftMultiply(in TColumn vec);

  static abstract TColumn operator *(in TSelf left, in TRow right);
  static abstract TRow operator *(in TColumn left, in TSelf right);
}