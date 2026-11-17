using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using PrimordialPower.Mathematics.Vectors;

namespace PrimordialPower.Mathematics.Matrices;

/// <summary>
/// Contains utility functions for matrix operations
/// </summary>
public static partial class Matrix
{
  public static TMatrix Create<TMatrix, TVecCol, TVecRow>(ReadOnlySpan<TVecRow> vectors)
  where TMatrix : IMatrixVectorComponents<TMatrix, TVecCol, TVecRow>, new()
  where TVecCol : IVector<TVecCol>
  where TVecRow : IVector<TVecRow>
  {
    TMatrix mat = new();
    for (int row = 0; row < TVecCol.DimCount; row++)
    {
      mat[row] = vectors[row];
    }
    return mat;
  }



  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static TTransposed Transpose<TMatrix, TTransposed>(in TMatrix matrix)
  where TMatrix : IMatrixTransposable<TMatrix, TTransposed>
  where TTransposed : IMatrixTransposable<TTransposed, TMatrix>
  => matrix.Transposed;

  public static TSquare Transpose<TSquare>(in TSquare matrix)
  where TSquare : IMatrixSquareInvertible<TSquare>
  => matrix.Transposed;



  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static bool TryInvert<TSquare>(in TSquare matrix, out TSquare inverted)
  where TSquare : IMatrixSquareInvertible<TSquare>
  => matrix.TryGetInverted(out inverted);



  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static TMatrix Add<TMatrix>(in TMatrix left, in TMatrix right)
  where TMatrix : IMatrix<TMatrix>
  => left + right;



  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static TMatrix Subtract<TMatrix>(in TMatrix left, in TMatrix right)
  where TMatrix : IMatrix<TMatrix>
  => left - right;



  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static TSquare Multiply<TSquare>(in TSquare left, in TSquare right)
  where TSquare : IMatrixSquareInvertible<TSquare>
  => left * right;

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static TResult Multiply<TMatrix, TOther, TResult>(in TMatrix left, in TOther right)
  where TMatrix : IMatrixHasMatrixProduct<TMatrix, TOther, TResult>
  where TOther : IMatrix<TOther>
  where TResult : IMatrix<TResult>
  => left * right;

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static TMatrix Multiply<TMatrix, TValue>(in TMatrix matrix, TValue scalar)
  where TMatrix : IMatrix<TMatrix, TValue>
  where TValue : INumber<TValue>
  => matrix * scalar;



  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static TColumn Transform<TMatrix, TColumn, TRow>(in TMatrix left, in TRow right)
  where TMatrix : IMatrixVectorComponents<TMatrix, TColumn, TRow>
  where TColumn : IVector<TColumn>
  where TRow : IVector<TRow>
  => left * right;

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static TRow Transform<TMatrix, TColumn, TRow>(in TColumn left, in TMatrix right)
  where TMatrix : IMatrixVectorComponents<TMatrix, TColumn, TRow>
  where TColumn : IVector<TColumn>
  where TRow : IVector<TRow>
  => left * right;



  public static string ToString<TMatrix, TValue>(in TMatrix matrix)
  where TMatrix : IMatrix<TMatrix, TValue>
  where TValue : INumber<TValue>
  {
    if (TMatrix.RowCount == 0 || TMatrix.ColumnCount == 0) return "(Empty)";

    string open = "(";
    string close = ")";
    string separator = ", ";

    StringBuilder str = new(1 + (int)TMatrix.RowCount + (int)TMatrix.ElementCount * 2);

    str.Append(open);

    for (uint x = 0; x < TMatrix.RowCount; x++)
    {
      str.Append(open);
      for (uint y = 0; y < TMatrix.ColumnCount; y++)
      {
        str.Append(matrix[x, y]);
        if (y < TMatrix.ColumnCount - 1)
        {
          str.Append(separator);
        }
      }
      str.Append(close);
      if (x < TMatrix.RowCount - 1)
      {
        str.Append(separator);
      }
    }

    str.Append(close);

    return str.ToString();
  }
}
