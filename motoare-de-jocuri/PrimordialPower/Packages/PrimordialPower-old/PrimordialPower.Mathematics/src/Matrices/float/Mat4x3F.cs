using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PrimordialPower.Mathematics.Vectors;
using PrimordialPower.Mathematics.ConstArrays;

namespace PrimordialPower.Mathematics.Matrices;

using ThisMatrix = Mat4x3F;
using ThisTransposed = Mat3x4F;
using ThisVectorRow = Vec3F;
using ThisVectorColumn = Vec4F;
using ThisValue = float;

[StructLayout(LayoutKind.Explicit)]
public struct Mat4x3F :
  IMatrix<ThisMatrix, ThisValue>,
  IMatrixTransposable<ThisMatrix, ThisTransposed>,
  IMatrixHasMatrixProduct<ThisMatrix, Mat3x2F, Mat4x2F>,
  IMatrixHasMatrixProduct<ThisMatrix, Mat3F, Mat4x3F>,
  IMatrixHasMatrixProduct<ThisMatrix, Mat3x4F, Mat4F>,
  IMatrixVectorComponents<ThisMatrix, ThisVectorColumn, ThisVectorRow>
{
  [FieldOffset(0)]
  private ConstArray4<ConstArray3<ThisValue>> array;



  [FieldOffset(sizeof(ThisValue) * 0)]
  public ThisValue M00;
  [FieldOffset(sizeof(ThisValue) * 1)]
  public ThisValue M01;
  [FieldOffset(sizeof(ThisValue) * 2)]
  public ThisValue M02;
  [FieldOffset(sizeof(ThisValue) * 3)]
  public ThisValue M10;
  [FieldOffset(sizeof(ThisValue) * 4)]
  public ThisValue M11;
  [FieldOffset(sizeof(ThisValue) * 5)]
  public ThisValue M12;
  [FieldOffset(sizeof(ThisValue) * 6)]
  public ThisValue M20;
  [FieldOffset(sizeof(ThisValue) * 7)]
  public ThisValue M21;
  [FieldOffset(sizeof(ThisValue) * 8)]
  public ThisValue M22;
  [FieldOffset(sizeof(ThisValue) * 9)]
  public ThisValue M30;
  [FieldOffset(sizeof(ThisValue) * 10)]
  public ThisValue M31;
  [FieldOffset(sizeof(ThisValue) * 11)]
  public ThisValue M32;



  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Mat4x3F(ThisValue m00, ThisValue m01, ThisValue m02, ThisValue m10, ThisValue m11, ThisValue m12, ThisValue m20, ThisValue m21, ThisValue m22, ThisValue m30, ThisValue m31, ThisValue m32)
  {
    M00 = m00; M01 = m01; M02 = m02;
    M10 = m10; M11 = m11; M12 = m12;
    M20 = m20; M21 = m21; M22 = m22;
    M30 = m30; M31 = m31; M32 = m32;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Mat4x3F(ThisValue diagonal)
  {
    M00 = diagonal; M01 = 0F; M02 = 0F;
    M10 = 0F; M11 = diagonal; M12 = 0F;
    M20 = 0F; M21 = 0F; M22 = diagonal;
    M30 = 0F; M31 = 0F; M32 = 0F;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Mat4x3F(in ThisMatrix other)
  {
    this = other;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Mat4x3F(in ThisVectorRow r0, in ThisVectorRow r1, in ThisVectorRow r2, in ThisVectorRow r3)
  {
    this.R0 = r0;
    this.R1 = r1;
    this.R2 = r2;
    this.R3 = r3;
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
    return new(this.array[row][0], this.array[row][1], this.array[row][2]);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisVectorRow GetRow(uint row) => GetRow((int)row);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public void SetRow(int row, in ThisVectorRow vec)
  {
    this.array[row][0] = vec.X;
    this.array[row][1] = vec.Y;
    this.array[row][2] = vec.Z;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public void SetRow(uint row, in ThisVectorRow vec) => SetRow((int)row, vec);



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisVectorColumn GetColumn(int col)
  {
    return new(this.array[0][col], this.array[1][col], this.array[2][col], this.array[3][col]);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisVectorColumn GetColumn(uint col) => GetColumn((int)col);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public void SetColumn(int col, in ThisVectorColumn vec)
  {
    this.array[0][col] = vec.X;
    this.array[1][col] = vec.Y;
    this.array[2][col] = vec.Z;
    this.array[3][col] = vec.W;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public void SetColumn(uint col, in ThisVectorColumn vec) => SetColumn((int)col, vec);



  public ThisVectorRow R0
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => GetRow(0);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => SetRow(0, value);
  }

  public ThisVectorRow R1
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => GetRow(1);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => SetRow(1, value);
  }

  public ThisVectorRow R2
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => GetRow(2);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => SetRow(2, value);
  }

  public ThisVectorRow R3
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => GetRow(3);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => SetRow(3, value);
  }

  public ThisVectorColumn C0
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => GetColumn(0);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => SetColumn(0, value);
  }

  public ThisVectorColumn C1
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => GetColumn(1);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => SetColumn(1, value);
  }

  public ThisVectorColumn C2
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => GetColumn(2);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => SetColumn(2, value);
  }



  public ThisTransposed Transposed
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    get => new(
      M00, M10, M20, M30,
      M01, M11, M21, M31,
      M02, M12, M22, M32
    );
  }



  public static ThisMatrix Identity
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => new(1F);
  }

  public static ThisMatrix Zero
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => new(0F);
  }



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisMatrix Neg()
  => new(
    -M00, -M01, -M02,
    -M10, -M11, -M12,
    -M20, -M21, -M22,
    -M30, -M31, -M32
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisMatrix Add(in ThisMatrix other)
  => new(
    M00 + other.M00, M01 + other.M01, M02 + other.M02,
    M10 + other.M10, M11 + other.M11, M12 + other.M12,
    M20 + other.M20, M21 + other.M21, M22 + other.M22,
    M30 + other.M30, M31 + other.M31, M32 + other.M32
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisMatrix Add(ThisValue scalar)
  => new(
    M00 + scalar, M01 + scalar, M02 + scalar,
    M10 + scalar, M11 + scalar, M12 + scalar,
    M20 + scalar, M21 + scalar, M22 + scalar,
    M30 + scalar, M31 + scalar, M32 + scalar
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisMatrix Subtract(in ThisMatrix other)
  => new(
    M00 - other.M00, M01 - other.M01, M02 - other.M02,
    M10 - other.M10, M11 - other.M11, M12 - other.M12,
    M20 - other.M20, M21 - other.M21, M22 - other.M22,
    M30 - other.M30, M31 - other.M31, M32 - other.M32
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisMatrix Subtract(ThisValue scalar)
  => new(
    M00 - scalar, M01 - scalar, M02 - scalar,
    M10 - scalar, M11 - scalar, M12 - scalar,
    M20 - scalar, M21 - scalar, M22 - scalar,
    M30 - scalar, M31 - scalar, M32 - scalar
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisMatrix CompMultiply(in ThisMatrix other)
  => new(
    M00 * other.M00, M01 * other.M01, M02 * other.M02,
    M10 * other.M10, M11 * other.M11, M12 * other.M12,
    M20 * other.M20, M21 * other.M21, M22 * other.M22,
    M30 * other.M30, M31 * other.M31, M32 * other.M32
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisMatrix CompMultiply(ThisValue scalar)
  => new(
    M00 * scalar, M01 * scalar, M02 * scalar,
    M10 * scalar, M11 * scalar, M12 * scalar,
    M20 * scalar, M21 * scalar, M22 * scalar,
    M30 * scalar, M31 * scalar, M32 * scalar
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisMatrix CompDivide(in ThisMatrix other)
  => new(
    M00 / other.M00, M01 / other.M01, M02 / other.M02,
    M10 / other.M10, M11 / other.M11, M12 / other.M12,
    M20 / other.M20, M21 / other.M21, M22 / other.M22,
    M30 / other.M30, M31 / other.M31, M32 / other.M32
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisMatrix CompDivide(ThisValue scalar)
  => new(
    M00 / scalar, M01 / scalar, M02 / scalar,
    M10 / scalar, M11 / scalar, M12 / scalar,
    M20 / scalar, M21 / scalar, M22 / scalar,
    M30 / scalar, M31 / scalar, M32 / scalar
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisMatrix Modulo(in ThisMatrix other)
  => new(
    M00 % other.M00, M01 % other.M01, M02 % other.M02,
    M10 % other.M10, M11 % other.M11, M12 % other.M12,
    M20 % other.M20, M21 % other.M21, M22 % other.M22,
    M30 % other.M30, M31 % other.M31, M32 % other.M32
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisVectorColumn Multiply(in ThisVectorRow vec)
  => new(
    M00 * vec.X + M01 * vec.Y + M02 * vec.Z,
    M10 * vec.X + M11 * vec.Y + M12 * vec.Z,
    M20 * vec.X + M21 * vec.Y + M22 * vec.Z,
    M30 * vec.X + M31 * vec.Y + M32 * vec.Z
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisVectorRow LeftMultiply(in ThisVectorColumn vec)
  => new(
    vec.X * M00 + vec.Y * M10 + vec.Z * M20 + vec.W * M30,
    vec.X * M01 + vec.Y * M11 + vec.Z * M21 + vec.W * M31,
    vec.X * M02 + vec.Y * M12 + vec.Z * M22 + vec.W * M32
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Mat4x2F Multiply(in Mat3x2F right)
  => new(
    M00 * right.M00 + M01 * right.M10 + M02 * right.M20,
    M00 * right.M01 + M01 * right.M11 + M02 * right.M21,
    M10 * right.M00 + M11 * right.M10 + M12 * right.M20,
    M10 * right.M01 + M11 * right.M11 + M12 * right.M21,
    M20 * right.M00 + M21 * right.M10 + M22 * right.M20,
    M20 * right.M01 + M21 * right.M11 + M22 * right.M21,
    M30 * right.M00 + M31 * right.M10 + M32 * right.M20,
    M30 * right.M01 + M31 * right.M11 + M32 * right.M21
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Mat4x3F Multiply(in Mat3F right)
  => new(
    M00 * right.M00 + M01 * right.M10 + M02 * right.M20,
    M00 * right.M01 + M01 * right.M11 + M02 * right.M21,
    M00 * right.M02 + M01 * right.M12 + M02 * right.M22,
    M10 * right.M00 + M11 * right.M10 + M12 * right.M20,
    M10 * right.M01 + M11 * right.M11 + M12 * right.M21,
    M10 * right.M02 + M11 * right.M12 + M12 * right.M22,
    M20 * right.M00 + M21 * right.M10 + M22 * right.M20,
    M20 * right.M01 + M21 * right.M11 + M22 * right.M21,
    M20 * right.M02 + M21 * right.M12 + M22 * right.M22,
    M30 * right.M00 + M31 * right.M10 + M32 * right.M20,
    M30 * right.M01 + M31 * right.M11 + M32 * right.M21,
    M30 * right.M02 + M31 * right.M12 + M32 * right.M22
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Mat4F Multiply(in Mat3x4F right)
  => new(
    M00 * right.M00 + M01 * right.M10 + M02 * right.M20,
    M00 * right.M01 + M01 * right.M11 + M02 * right.M21,
    M00 * right.M02 + M01 * right.M12 + M02 * right.M22,
    M00 * right.M03 + M01 * right.M13 + M02 * right.M23,
    M10 * right.M00 + M11 * right.M10 + M12 * right.M20,
    M10 * right.M01 + M11 * right.M11 + M12 * right.M21,
    M10 * right.M02 + M11 * right.M12 + M12 * right.M22,
    M10 * right.M03 + M11 * right.M13 + M12 * right.M23,
    M20 * right.M00 + M21 * right.M10 + M22 * right.M20,
    M20 * right.M01 + M21 * right.M11 + M22 * right.M21,
    M20 * right.M02 + M21 * right.M12 + M22 * right.M22,
    M20 * right.M03 + M21 * right.M13 + M22 * right.M23,
    M30 * right.M00 + M31 * right.M10 + M32 * right.M20,
    M30 * right.M01 + M31 * right.M11 + M32 * right.M21,
    M30 * right.M02 + M31 * right.M12 + M32 * right.M22,
    M30 * right.M03 + M31 * right.M13 + M32 * right.M23
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
  public static Mat4x2F operator *(in ThisMatrix left, in Mat3x2F right)
  => left.Multiply(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Mat4x3F operator *(in ThisMatrix left, in Mat3F right)
  => left.Multiply(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Mat4F operator *(in ThisMatrix left, in Mat3x4F right)
  => left.Multiply(right);



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static implicit operator ThisMatrix(Mat4x3<ThisValue> other)
  => new(
    other.M00, other.M01, other.M02,
    other.M10, other.M11, other.M12,
    other.M20, other.M21, other.M22,
    other.M30, other.M31, other.M32
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static implicit operator Mat4x3<ThisValue>(ThisMatrix other)
  => new(
    other.M00, other.M01, other.M02,
    other.M10, other.M11, other.M12,
    other.M20, other.M21, other.M22,
    other.M30, other.M31, other.M32
  );



  public override string ToString() => Matrix.ToString<ThisMatrix, ThisValue>(this);
}
