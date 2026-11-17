using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PrimordialPower.Mathematics.Vectors;
using PrimordialPower.Mathematics.ConstArrays;

namespace PrimordialPower.Mathematics.Matrices;

using ThisMatrix = Mat3x4D;
using ThisTransposed = Mat4x3D;
using ThisVectorRow = Vec4D;
using ThisVectorColumn = Vec3D;
using ThisValue = double;

[StructLayout(LayoutKind.Explicit)]
public struct Mat3x4D :
  IMatrix<ThisMatrix, ThisValue>,
  IMatrixTransposable<ThisMatrix, ThisTransposed>,
  IMatrixHasMatrixProduct<ThisMatrix, Mat4x2D, Mat3x2D>,
  IMatrixHasMatrixProduct<ThisMatrix, Mat4x3D, Mat3D>,
  IMatrixHasMatrixProduct<ThisMatrix, Mat4D, Mat3x4D>,
  IMatrixVectorComponents<ThisMatrix, ThisVectorColumn, ThisVectorRow>
{
  [FieldOffset(0)]
  private ConstArray3<ConstArray4<ThisValue>> array;



  [FieldOffset(sizeof(ThisValue) * 0)]
  public ThisValue M00;
  [FieldOffset(sizeof(ThisValue) * 1)]
  public ThisValue M01;
  [FieldOffset(sizeof(ThisValue) * 2)]
  public ThisValue M02;
  [FieldOffset(sizeof(ThisValue) * 3)]
  public ThisValue M03;
  [FieldOffset(sizeof(ThisValue) * 4)]
  public ThisValue M10;
  [FieldOffset(sizeof(ThisValue) * 5)]
  public ThisValue M11;
  [FieldOffset(sizeof(ThisValue) * 6)]
  public ThisValue M12;
  [FieldOffset(sizeof(ThisValue) * 7)]
  public ThisValue M13;
  [FieldOffset(sizeof(ThisValue) * 8)]
  public ThisValue M20;
  [FieldOffset(sizeof(ThisValue) * 9)]
  public ThisValue M21;
  [FieldOffset(sizeof(ThisValue) * 10)]
  public ThisValue M22;
  [FieldOffset(sizeof(ThisValue) * 11)]
  public ThisValue M23;



  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Mat3x4D(ThisValue m00, ThisValue m01, ThisValue m02, ThisValue m03, ThisValue m10, ThisValue m11, ThisValue m12, ThisValue m13, ThisValue m20, ThisValue m21, ThisValue m22, ThisValue m23)
  {
    M00 = m00; M01 = m01; M02 = m02; M03 = m03;
    M10 = m10; M11 = m11; M12 = m12; M13 = m13;
    M20 = m20; M21 = m21; M22 = m22; M23 = m23;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Mat3x4D(ThisValue diagonal)
  {
    M00 = diagonal; M01 = 0D; M02 = 0D; M03 = 0D;
    M10 = 0D; M11 = diagonal; M12 = 0D; M13 = 0D;
    M20 = 0D; M21 = 0D; M22 = diagonal; M23 = 0D;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Mat3x4D(in ThisMatrix other)
  {
    this = other;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Mat3x4D(in ThisVectorRow r0, in ThisVectorRow r1, in ThisVectorRow r2)
  {
    this.R0 = r0;
    this.R1 = r1;
    this.R2 = r2;
  }



  public static uint RowCount => ThisVectorColumn.DimCount;
  public uint Rows => RowCount;

  public static uint ColumnCount => ThisVectorRow.DimCount;
  public uint Columns => ColumnCount;

  public static uint ElementCount => 12;
  public uint Elements => ElementCount;



  public ThisValue this[int row, int col]
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    get => this.array[row][col];
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
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
    return new(this.array[row][0], this.array[row][1], this.array[row][2], this.array[row][3]);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisVectorRow GetRow(uint row) => GetRow((int)row);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public void SetRow(int row, in ThisVectorRow vec)
  {
    this.array[row][0] = vec.X;
    this.array[row][1] = vec.Y;
    this.array[row][2] = vec.Z;
    this.array[row][3] = vec.W;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public void SetRow(uint row, in ThisVectorRow vec) => SetRow((int)row, vec);



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisVectorColumn GetColumn(int col)
  {
    return new(this.array[0][col], this.array[1][col], this.array[2][col]);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisVectorColumn GetColumn(uint col) => GetColumn((int)col);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public void SetColumn(int col, in ThisVectorColumn vec)
  {
    this.array[0][col] = vec.X;
    this.array[1][col] = vec.Y;
    this.array[2][col] = vec.Z;
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

  public ThisVectorRow R2
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    get => GetRow(2);
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    set => SetRow(2, value);
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

  public ThisVectorColumn C2
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    get => GetColumn(2);
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    set => SetColumn(2, value);
  }

  public ThisVectorColumn C3
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    get => GetColumn(3);
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    set => SetColumn(3, value);
  }



  public ThisTransposed Transposed
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    get => new ThisTransposed(
      M00, M10, M20,
      M01, M11, M21,
      M02, M12, M22,
      M03, M13, M23
    );
  }



  public static ThisMatrix Identity
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => new(1D);
  }

  public static ThisMatrix Zero
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => new(0D);
  }



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisMatrix Neg()
  => new(
    -M00, -M01, -M02, -M03,
    -M10, -M11, -M12, -M13,
    -M20, -M21, -M22, -M23
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisMatrix Add(in ThisMatrix other)
  => new(
    M00 + other.M00, M01 + other.M01, M02 + other.M02, M03 + other.M03,
    M10 + other.M10, M11 + other.M11, M12 + other.M12, M13 + other.M13,
    M20 + other.M20, M21 + other.M21, M22 + other.M22, M23 + other.M23
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisMatrix Add(ThisValue scalar)
  => new(
    M00 + scalar, M01 + scalar, M02 + scalar, M03 + scalar,
    M10 + scalar, M11 + scalar, M12 + scalar, M13 + scalar,
    M20 + scalar, M21 + scalar, M22 + scalar, M23 + scalar
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisMatrix Subtract(in ThisMatrix other)
  => new(
    M00 - other.M00, M01 - other.M01, M02 - other.M02, M03 - other.M03,
    M10 - other.M10, M11 - other.M11, M12 - other.M12, M13 - other.M13,
    M20 - other.M20, M21 - other.M21, M22 - other.M22, M23 - other.M23
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisMatrix Subtract(ThisValue scalar)
  => new(
    M00 - scalar, M01 - scalar, M02 - scalar, M03 - scalar,
    M10 - scalar, M11 - scalar, M12 - scalar, M13 - scalar,
    M20 - scalar, M21 - scalar, M22 - scalar, M23 - scalar
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisMatrix CompMultiply(in ThisMatrix other)
  => new(
    M00 * other.M00, M01 * other.M01, M02 * other.M02, M03 * other.M03,
    M10 * other.M10, M11 * other.M11, M12 * other.M12, M13 * other.M13,
    M20 * other.M20, M21 * other.M21, M22 * other.M22, M23 * other.M23
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisMatrix CompMultiply(ThisValue scalar)
  => new(
    M00 * scalar, M01 * scalar, M02 * scalar, M03 * scalar,
    M10 * scalar, M11 * scalar, M12 * scalar, M13 * scalar,
    M20 * scalar, M21 * scalar, M22 * scalar, M23 * scalar
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisMatrix CompDivide(in ThisMatrix other)
  => new(
    M00 / other.M00, M01 / other.M01, M02 / other.M02, M03 / other.M03,
    M10 / other.M10, M11 / other.M11, M12 / other.M12, M13 / other.M13,
    M20 / other.M20, M21 / other.M21, M22 / other.M22, M23 / other.M23
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisMatrix CompDivide(ThisValue scalar)
  => new(
    M00 / scalar, M01 / scalar, M02 / scalar, M03 / scalar,
    M10 / scalar, M11 / scalar, M12 / scalar, M13 / scalar,
    M20 / scalar, M21 / scalar, M22 / scalar, M23 / scalar
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisMatrix Modulo(in ThisMatrix other)
  => new(
    M00 % other.M00, M01 % other.M01, M02 % other.M02, M03 % other.M03,
    M10 % other.M10, M11 % other.M11, M12 % other.M12, M13 % other.M13,
    M20 % other.M20, M21 % other.M21, M22 % other.M22, M23 % other.M23
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisVectorColumn Multiply(in ThisVectorRow vec)
  => new(
    M00 * vec.X + M01 * vec.Y + M02 * vec.Z + M03 * vec.W,
    M10 * vec.X + M11 * vec.Y + M12 * vec.Z + M13 * vec.W,
    M20 * vec.X + M21 * vec.Y + M22 * vec.Z + M23 * vec.W
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisVectorRow LeftMultiply(in ThisVectorColumn vec)
  => new(
    vec.X * M00 + vec.Y * M10 + vec.Z * M20,
    vec.X * M01 + vec.Y * M11 + vec.Z * M21,
    vec.X * M02 + vec.Y * M12 + vec.Z * M22,
    vec.X * M03 + vec.Y * M13 + vec.Z * M23
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Mat3x2D Multiply(in Mat4x2D right)
  => new(
    M00 * right.M00 + M01 * right.M10 + M02 * right.M20 + M03 * right.M30,
    M00 * right.M01 + M01 * right.M11 + M02 * right.M21 + M03 * right.M31,
    M10 * right.M00 + M11 * right.M10 + M12 * right.M20 + M13 * right.M30,
    M10 * right.M01 + M11 * right.M11 + M12 * right.M21 + M13 * right.M31,
    M20 * right.M00 + M21 * right.M10 + M22 * right.M20 + M23 * right.M30,
    M20 * right.M01 + M21 * right.M11 + M22 * right.M21 + M23 * right.M31
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Mat3D Multiply(in Mat4x3D right)
  => new(
    M00 * right.M00 + M01 * right.M10 + M02 * right.M20 + M03 * right.M30,
    M00 * right.M01 + M01 * right.M11 + M02 * right.M21 + M03 * right.M31,
    M00 * right.M02 + M01 * right.M12 + M02 * right.M22 + M03 * right.M32,
    M10 * right.M00 + M11 * right.M10 + M12 * right.M20 + M13 * right.M30,
    M10 * right.M01 + M11 * right.M11 + M12 * right.M21 + M13 * right.M31,
    M10 * right.M02 + M11 * right.M12 + M12 * right.M22 + M13 * right.M32,
    M20 * right.M00 + M21 * right.M10 + M22 * right.M20 + M23 * right.M30,
    M20 * right.M01 + M21 * right.M11 + M22 * right.M21 + M23 * right.M31,
    M20 * right.M02 + M21 * right.M12 + M22 * right.M22 + M23 * right.M32
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Mat3x4D Multiply(in Mat4D right)
  => new(
    M00 * right.M00 + M01 * right.M10 + M02 * right.M20 + M03 * right.M30,
    M00 * right.M01 + M01 * right.M11 + M02 * right.M21 + M03 * right.M31,
    M00 * right.M02 + M01 * right.M12 + M02 * right.M22 + M03 * right.M32,
    M00 * right.M03 + M01 * right.M13 + M02 * right.M23 + M03 * right.M33,
    M10 * right.M00 + M11 * right.M10 + M12 * right.M20 + M13 * right.M30,
    M10 * right.M01 + M11 * right.M11 + M12 * right.M21 + M13 * right.M31,
    M10 * right.M02 + M11 * right.M12 + M12 * right.M22 + M13 * right.M32,
    M10 * right.M03 + M11 * right.M13 + M12 * right.M23 + M13 * right.M33,
    M20 * right.M00 + M21 * right.M10 + M22 * right.M20 + M23 * right.M30,
    M20 * right.M01 + M21 * right.M11 + M22 * right.M21 + M23 * right.M31,
    M20 * right.M02 + M21 * right.M12 + M22 * right.M22 + M23 * right.M32,
    M20 * right.M03 + M21 * right.M13 + M22 * right.M23 + M23 * right.M33
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
  public static Mat3x2D operator *(in ThisMatrix left, in Mat4x2D right)
  => left.Multiply(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Mat3D operator *(in ThisMatrix left, in Mat4x3D right)
  => left.Multiply(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Mat3x4D operator *(in ThisMatrix left, in Mat4D right)
  => left.Multiply(right);



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static implicit operator ThisMatrix(Mat3x4<ThisValue> other)
  => new(
    other.M00, other.M01, other.M02, other.M03,
    other.M10, other.M11, other.M12, other.M13,
    other.M20, other.M21, other.M22, other.M23
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static implicit operator Mat3x4<ThisValue>(ThisMatrix other)
  => new(
    other.M00, other.M01, other.M02, other.M03,
    other.M10, other.M11, other.M12, other.M13,
    other.M20, other.M21, other.M22, other.M23
  );



  public override string ToString() => Matrix.ToString<ThisMatrix, ThisValue>(this);
}
