using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PrimordialPower.Mathematics.Vectors;
using PrimordialPower.Mathematics.ConstArrays;

namespace PrimordialPower.Mathematics.Matrices;

using ThisMatrix = Mat2I;
using ThisVectorRow = Vec2I;
using ThisVectorColumn = Vec2I;
using ThisValue = int;

[StructLayout(LayoutKind.Explicit)]
public struct Mat2I :
  IMatrixSquare<ThisMatrix, ThisValue>,
  IMatrixHasMatrixProduct<ThisMatrix, Mat2x3I, Mat2x3I>,
  IMatrixHasMatrixProduct<ThisMatrix, Mat2x4I, Mat2x4I>,
  IMatrixVectorComponents<ThisMatrix, ThisVectorColumn, ThisVectorRow>
{
  [FieldOffset(0)]
  private ConstArray2<ConstArray2<ThisValue>> array;



  [FieldOffset(sizeof(ThisValue) * 0)]
  public ThisValue M00;
  [FieldOffset(sizeof(ThisValue) * 1)]
  public ThisValue M01;
  [FieldOffset(sizeof(ThisValue) * 2)]
  public ThisValue M10;
  [FieldOffset(sizeof(ThisValue) * 3)]
  public ThisValue M11;



  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Mat2I(ThisValue m00, ThisValue m01, ThisValue m10, ThisValue m11)
  {
    M00 = m00; M01 = m01;
    M10 = m10; M11 = m11;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Mat2I(ThisValue diagonal)
  {
    M00 = diagonal; M01 = 0;
    M10 = 0; M11 = diagonal;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Mat2I(in ThisMatrix other)
  {
    this = other;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Mat2I(in ThisVectorRow r0, in ThisVectorRow r1)
  {
    this.R0 = r0;
    this.R1 = r1;
  }



  public static uint DimCount => ThisVectorRow.DimCount;
  public uint Dimension => DimCount;

  public static uint RowCount => ThisVectorColumn.DimCount;
  public uint Rows => RowCount;

  public static uint ColumnCount => ThisVectorRow.DimCount;
  public uint Columns => ColumnCount;

  public static uint ElementCount => 4;
  public uint Elements => ElementCount;



  public ThisValue this[int row, int col]
  {
    get => this.array[row][col];
    set => this.array[row][col] = value;
  }

  public ThisValue this[uint row, uint col]
  {
    get => this[(int)row, (int)col];
    set => this[(int)row, (int)col] = value;
  }

  public ThisVectorRow this[uint row]
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    get => GetRow(row);
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    set => SetRow(row, value);
  }

  public ThisVectorRow this[int row]
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    get => GetRow(row);
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    set => SetRow(row, value);
  }



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisVectorRow GetRow(int row)
  {
    return new(this.array[row][0], this.array[row][1]);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisVectorRow GetRow(uint row) => GetRow((int)row);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public void SetRow(int row, in ThisVectorRow vec)
  {
    this.array[row][0] = vec.X;
    this.array[row][1] = vec.Y;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public void SetRow(uint row, in ThisVectorRow vec) => SetRow((int)row, vec);



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisVectorColumn GetColumn(int col)
  {
    return new(this.array[0][col], this.array[1][col]);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisVectorColumn GetColumn(uint col) => GetColumn((int)col);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public void SetColumn(int col, in ThisVectorColumn vec)
  {
    this.array[0][col] = vec.X;
    this.array[1][col] = vec.Y;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public void SetColumn(uint col, in ThisVectorColumn vec) => SetColumn((int)col, vec);



  public ThisVectorRow R0
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    get => GetRow(0);
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    set => SetRow(0, value);
  }

  public ThisVectorRow R1
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    get => GetRow(1);
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    set => SetRow(1, value);
  }

  public ThisVectorColumn C0
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    get => GetColumn(0);
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    set => SetColumn(0, value);
  }

  public ThisVectorColumn C1
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    get => GetColumn(1);
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    set => SetColumn(1, value);
  }



  public ThisValue Determinant
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    get => M00 * M11 - M01 * M10;
  }

  public ThisValue Trace
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    get => M00 + M11;
  }



  public ThisMatrix Transposed
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    get => new(
      M00, M10,
      M01, M11
    );
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public void Transpose()
  {
    this = this.Transposed;
  }



  public static ThisMatrix Identity
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => new(1);
  }

  public static ThisMatrix Zero
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => new(0);
  }



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisMatrix Neg()
  => new(
    -M00, -M01,
    -M10, -M11
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisMatrix Add(in ThisMatrix other)
  => new(
    M00 + other.M00, M01 + other.M01,
    M10 + other.M10, M11 + other.M11
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisMatrix Add(ThisValue scalar)
  => new(
    M00 + scalar, M01 + scalar,
    M10 + scalar, M11 + scalar
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisMatrix Subtract(in ThisMatrix other)
  => new(
    M00 - other.M00, M01 - other.M01,
    M10 - other.M10, M11 - other.M11
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisMatrix Subtract(ThisValue scalar)
  => new(
    M00 - scalar, M01 - scalar,
    M10 - scalar, M11 - scalar
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisMatrix CompMultiply(in ThisMatrix other)
  => new(
    M00 * other.M00, M01 * other.M01,
    M10 * other.M10, M11 * other.M11
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisMatrix CompMultiply(ThisValue scalar)
  => new(
    M00 * scalar, M01 * scalar,
    M10 * scalar, M11 * scalar
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisMatrix CompDivide(in ThisMatrix other)
  => new(
    M00 / other.M00, M01 / other.M01,
    M10 / other.M10, M11 / other.M11
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisMatrix CompDivide(ThisValue scalar)
  => new(
    M00 / scalar, M01 / scalar,
    M10 / scalar, M11 / scalar
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisMatrix Modulo(in ThisMatrix other)
  => new(
    M00 % other.M00, M01 % other.M01,
    M10 % other.M10, M11 % other.M11
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisVectorColumn Multiply(in ThisVectorRow vec)
  => new(
    M00 * vec.X + M01 * vec.Y,
    M10 * vec.X + M11 * vec.Y
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisVectorRow LeftMultiply(in ThisVectorColumn vec)
  => new(
    vec.X * M00 + vec.Y * M10,
    vec.X * M01 + vec.Y * M11
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisMatrix Multiply(in ThisMatrix other)
  => new(
    M00 * other.M00 + M01 * other.M10,
    M00 * other.M01 + M01 * other.M11,

    M10 * other.M00 + M11 * other.M10,
    M10 * other.M01 + M11 * other.M11
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Mat2x3I Multiply(in Mat2x3I right)
  => new(
    M00 * right.M00 + M01 * right.M10,
    M00 * right.M01 + M01 * right.M11,
    M00 * right.M02 + M01 * right.M12,
    M10 * right.M00 + M11 * right.M10,
    M10 * right.M01 + M11 * right.M11,
    M10 * right.M02 + M11 * right.M12
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Mat2x4I Multiply(in Mat2x4I right)
  => new(
    M00 * right.M00 + M01 * right.M10,
    M00 * right.M01 + M01 * right.M11,
    M00 * right.M02 + M01 * right.M12,
    M00 * right.M03 + M01 * right.M13,
    M10 * right.M00 + M11 * right.M10,
    M10 * right.M01 + M11 * right.M11,
    M10 * right.M02 + M11 * right.M12,
    M10 * right.M03 + M11 * right.M13
  );



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static ThisMatrix operator -(in ThisMatrix right)
  => right.Neg();



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static ThisMatrix operator +(in ThisMatrix left, in ThisMatrix right)
  => left.Add(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static ThisMatrix operator +(in ThisMatrix left, ThisValue right)
  => left.Add(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static ThisMatrix operator +(ThisValue left, in ThisMatrix right)
  => right.Add(left);



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static ThisMatrix operator -(in ThisMatrix left, in ThisMatrix right)
  => left.Subtract(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static ThisMatrix operator -(in ThisMatrix left, ThisValue right)
  => left.Subtract(right);



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static ThisMatrix operator %(in ThisMatrix left, in ThisMatrix right)
  => left.Modulo(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static ThisMatrix operator *(in ThisMatrix left, in ThisMatrix right)
  => left.Multiply(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static ThisMatrix operator *(in ThisMatrix left, ThisValue right)
  => left.CompMultiply(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static ThisMatrix operator *(ThisValue left, in ThisMatrix right)
  => right.CompMultiply(left);



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static ThisMatrix operator /(in ThisMatrix left, ThisValue right)
  => left.CompDivide(right);



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static ThisVectorRow operator *(in ThisVectorColumn left, in ThisMatrix right)
  => right.LeftMultiply(left);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static ThisVectorColumn operator *(in ThisMatrix left, in ThisVectorRow right)
  => left.Multiply(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Mat2x3I operator *(in ThisMatrix left, in Mat2x3I right)
  => left.Multiply(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Mat2x4I operator *(in ThisMatrix left, in Mat2x4I right)
  => left.Multiply(right);



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static implicit operator ThisMatrix(Mat2<ThisValue> other)
  => new(
    other.M00, other.M01,
    other.M10, other.M11
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static implicit operator Mat2<ThisValue>(ThisMatrix other)
  => new(
    other.M00, other.M01,
    other.M10, other.M11
  );



  public override string ToString() => Matrix.ToString<ThisMatrix, ThisValue>(this);
}
