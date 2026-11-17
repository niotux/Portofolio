using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PrimordialPower.Mathematics.Vectors;
using PrimordialPower.Mathematics.ConstArrays;

namespace PrimordialPower.Mathematics.Matrices;

using ThisMatrix = Mat4F;
using ThisVectorRow = Vec4F;
using ThisVectorColumn = Vec4F;
using ThisValue = float;

[StructLayout(LayoutKind.Explicit)]
public struct Mat4F :
  IMatrixSquare<ThisMatrix, ThisValue>,
  IMatrixSquareInvertible<ThisMatrix>,
  IMatrixHasMatrixProduct<ThisMatrix, Mat4x2F, Mat4x2F>,
  IMatrixHasMatrixProduct<ThisMatrix, Mat4x3F, Mat4x3F>,
  IMatrixVectorComponents<ThisMatrix, ThisVectorColumn, ThisVectorRow>
{
  [FieldOffset(0)]
  private ConstArray4<ConstArray4<ThisValue>> array;



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
  [FieldOffset(sizeof(ThisValue) * 12)]
  public ThisValue M30;
  [FieldOffset(sizeof(ThisValue) * 13)]
  public ThisValue M31;
  [FieldOffset(sizeof(ThisValue) * 14)]
  public ThisValue M32;
  [FieldOffset(sizeof(ThisValue) * 15)]
  public ThisValue M33;



  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Mat4F(
    ThisValue m00, ThisValue m01, ThisValue m02, ThisValue m03,
    ThisValue m10, ThisValue m11, ThisValue m12, ThisValue m13,
    ThisValue m20, ThisValue m21, ThisValue m22, ThisValue m23,
    ThisValue m30, ThisValue m31, ThisValue m32, ThisValue m33
  )
  {
    M00 = m00; M01 = m01; M02 = m02; M03 = m03;
    M10 = m10; M11 = m11; M12 = m12; M13 = m13;
    M20 = m20; M21 = m21; M22 = m22; M23 = m23;
    M30 = m30; M31 = m31; M32 = m32; M33 = m33;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Mat4F(ThisValue diagonal)
  {
    M00 = diagonal; M01 = 0F; M02 = 0F; M03 = 0F;
    M10 = 0F; M11 = diagonal; M12 = 0F; M13 = 0F;
    M20 = 0F; M21 = 0F; M22 = diagonal; M23 = 0F;
    M30 = 0F; M31 = 0F; M32 = 0F; M33 = diagonal;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Mat4F(in ThisMatrix other)
  {
    this = other;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Mat4F(in ThisVectorRow r0, in ThisVectorRow r1, in ThisVectorRow r2, in ThisVectorRow r3)
  {
    this.R0 = r0;
    this.R1 = r1;
    this.R2 = r2;
    this.R3 = r3;
  }



  public static uint DimCount => ThisVectorRow.DimCount;
  public uint Dimension => DimCount;

  public static uint RowCount => ThisVectorColumn.DimCount;
  public uint Rows => RowCount;

  public static uint ColumnCount => ThisVectorRow.DimCount;
  public uint Columns => ColumnCount;

  public static uint ElementCount => 16;
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

  public ThisVectorColumn C3
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => GetColumn(3);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => SetColumn(3, value);
  }



  public ThisValue Determinant
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    get
    {
      ThisValue det0 = M11 * (M22 * M33 - M23 * M32) - M12 * (M21 * M33 - M23 * M31) + M13 * (M21 * M32 - M22 * M31);
      ThisValue det1 = M10 * (M22 * M33 - M23 * M32) - M12 * (M20 * M33 - M23 * M30) + M13 * (M20 * M32 - M22 * M30);
      ThisValue det2 = M10 * (M21 * M33 - M23 * M31) - M11 * (M20 * M33 - M23 * M30) + M13 * (M20 * M31 - M21 * M30);
      ThisValue det3 = M10 * (M21 * M32 - M22 * M31) - M11 * (M20 * M32 - M22 * M30) + M12 * (M20 * M31 - M21 * M30);

      return M00 * det0 - M01 * det1 + M02 * det2 - M03 * det3;
    }
  }

  public ThisValue Trace
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    get => M00 + M11 + M22 + M33;
  }



  public ThisMatrix Transposed
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    get => new(
      M00, M10, M20, M30,
      M01, M11, M21, M31,
      M02, M12, M22, M32,
      M03, M13, M23, M33
    );
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public void Transpose()
  {
    this = this.Transposed;
  }



  [MethodImpl(MethodImplOptions.AggressiveOptimization)]
  public bool TryGetInverted(out ThisMatrix inverted)
  {
    ThisValue a00 = M22 * M33 - M23 * M32;
    ThisValue a01 = M21 * M33 - M23 * M31;
    ThisValue a02 = M21 * M32 - M22 * M31;
    ThisValue a03 = M20 * M33 - M23 * M30;
    ThisValue a04 = M20 * M32 - M22 * M30;
    ThisValue a05 = M20 * M31 - M21 * M30;

    ThisValue a10 = M12 * M33 - M13 * M32;
    ThisValue a11 = M11 * M33 - M13 * M31;
    ThisValue a12 = M11 * M32 - M12 * M31;
    ThisValue a13 = M10 * M33 - M13 * M30;
    ThisValue a14 = M10 * M32 - M12 * M30;
    ThisValue a15 = M10 * M31 - M11 * M30;

    ThisValue a20 = M12 * M23 - M13 * M22;
    ThisValue a21 = M11 * M23 - M13 * M21;
    ThisValue a22 = M11 * M22 - M12 * M21;
    ThisValue a23 = M10 * M23 - M13 * M20;
    ThisValue a24 = M10 * M22 - M12 * M20;
    ThisValue a25 = M10 * M21 - M11 * M20;

    ThisValue det =
    M00 * (M11 * a00 - M12 * a01 + M13 * a02) -
    M01 * (M10 * a00 - M12 * a03 + M13 * a04) +
    M02 * (M10 * a01 - M11 * a03 + M13 * a05) -
    M03 * (M10 * a02 - M11 * a04 + M12 * a05);

    if (ThisValue.Abs(det) < ThisValue.Epsilon)
    {
      inverted = this;
      return false;
    }

    ThisValue invDet = 1F / det;

    inverted = new(
      (M11 * a00 - M12 * a01 + M13 * a02) * invDet,
      (-M01 * a00 + M02 * a01 - M03 * a02) * invDet,
      (M01 * a10 - M02 * a11 + M03 * a12) * invDet,
      (-M01 * a20 + M02 * a21 - M03 * a22) * invDet,

      (-M10 * a00 + M12 * a03 - M13 * a04) * invDet,
      (M00 * a00 - M02 * a03 + M03 * a04) * invDet,
      (-M00 * a10 + M02 * a13 - M03 * a14) * invDet,
      (M00 * a20 - M02 * a23 + M03 * a24) * invDet,

      (M10 * a01 - M11 * a03 + M13 * a05) * invDet,
      (-M00 * a01 + M01 * a03 - M03 * a05) * invDet,
      (M00 * a11 - M01 * a13 + M03 * a15) * invDet,
      (-M00 * a21 + M01 * a23 - M03 * a25) * invDet,

      (-M10 * a02 + M11 * a04 - M12 * a05) * invDet,
      (M00 * a02 - M01 * a04 + M02 * a05) * invDet,
      (-M00 * a12 + M01 * a14 - M02 * a15) * invDet,
      (M00 * a22 - M01 * a24 + M02 * a25) * invDet
    );
    return true;
  }



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public bool TryInvert()
  {
    return this.TryGetInverted(out this);
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
    -M00, -M01, -M02, -M03,
    -M10, -M11, -M12, -M13,
    -M20, -M21, -M22, -M23,
    -M30, -M31, -M32, -M33
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisMatrix Add(in ThisMatrix other)
  => new(
    M00 + other.M00, M01 + other.M01, M02 + other.M02, M03 + other.M03,
    M10 + other.M10, M11 + other.M11, M12 + other.M12, M13 + other.M13,
    M20 + other.M20, M21 + other.M21, M22 + other.M22, M23 + other.M23,
    M30 + other.M30, M31 + other.M31, M32 + other.M32, M33 + other.M33
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisMatrix Add(ThisValue scalar)
  => new(
    M00 + scalar, M01 + scalar, M02 + scalar, M03 + scalar,
    M10 + scalar, M11 + scalar, M12 + scalar, M13 + scalar,
    M20 + scalar, M21 + scalar, M22 + scalar, M23 + scalar,
    M30 + scalar, M31 + scalar, M32 + scalar, M33 + scalar
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisMatrix Subtract(in ThisMatrix other)
  => new(
    M00 - other.M00, M01 - other.M01, M02 - other.M02, M03 - other.M03,
    M10 - other.M10, M11 - other.M11, M12 - other.M12, M13 - other.M13,
    M20 - other.M20, M21 - other.M21, M22 - other.M22, M23 - other.M23,
    M30 - other.M30, M31 - other.M31, M32 - other.M32, M33 - other.M33
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisMatrix Subtract(ThisValue scalar)
  => new(
    M00 - scalar, M01 - scalar, M02 - scalar, M03 - scalar,
    M10 - scalar, M11 - scalar, M12 - scalar, M13 - scalar,
    M20 - scalar, M21 - scalar, M22 - scalar, M23 - scalar,
    M30 - scalar, M31 - scalar, M32 - scalar, M33 - scalar
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisMatrix CompMultiply(in ThisMatrix other)
  => new(
    M00 * other.M00, M01 * other.M01, M02 * other.M02, M03 * other.M03,
    M10 * other.M10, M11 * other.M11, M12 * other.M12, M13 * other.M13,
    M20 * other.M20, M21 * other.M21, M22 * other.M22, M23 * other.M23,
    M30 * other.M30, M31 * other.M31, M32 * other.M32, M33 * other.M33
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisMatrix CompMultiply(ThisValue scalar)
  => new(
    M00 * scalar, M01 * scalar, M02 * scalar, M03 * scalar,
    M10 * scalar, M11 * scalar, M12 * scalar, M13 * scalar,
    M20 * scalar, M21 * scalar, M22 * scalar, M23 * scalar,
    M30 * scalar, M31 * scalar, M32 * scalar, M33 * scalar
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisMatrix CompDivide(in ThisMatrix other)
  => new(
    M00 / other.M00, M01 / other.M01, M02 / other.M02, M03 / other.M03,
    M10 / other.M10, M11 / other.M11, M12 / other.M12, M13 / other.M13,
    M20 / other.M20, M21 / other.M21, M22 / other.M22, M23 / other.M23,
    M30 / other.M30, M31 / other.M31, M32 / other.M32, M33 / other.M33
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisMatrix CompDivide(ThisValue scalar)
  => new(
    M00 / scalar, M01 / scalar, M02 / scalar, M03 / scalar,
    M10 / scalar, M11 / scalar, M12 / scalar, M13 / scalar,
    M20 / scalar, M21 / scalar, M22 / scalar, M23 / scalar,
    M30 / scalar, M31 / scalar, M32 / scalar, M33 / scalar
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisMatrix Modulo(in ThisMatrix other)
  => new(
    M00 % other.M00, M01 % other.M01, M02 % other.M02, M03 % other.M03,
    M10 % other.M10, M11 % other.M11, M12 % other.M12, M13 % other.M13,
    M20 % other.M20, M21 % other.M21, M22 % other.M22, M23 % other.M23,
    M30 % other.M30, M31 % other.M31, M32 % other.M32, M33 % other.M33
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisVectorColumn Multiply(in ThisVectorRow vec)
  => new(
    M00 * vec.X + M01 * vec.Y + M02 * vec.Z + M03 * vec.W,
    M10 * vec.X + M11 * vec.Y + M12 * vec.Z + M13 * vec.W,
    M20 * vec.X + M21 * vec.Y + M22 * vec.Z + M23 * vec.W,
    M30 * vec.X + M31 * vec.Y + M32 * vec.Z + M33 * vec.W
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisVectorRow LeftMultiply(in ThisVectorColumn vec)
  => new(
    vec.X * M00 + vec.Y * M10 + vec.Z * M20 + vec.W * M30,
    vec.X * M01 + vec.Y * M11 + vec.Z * M21 + vec.W * M31,
    vec.X * M02 + vec.Y * M12 + vec.Z * M22 + vec.W * M32,
    vec.X * M03 + vec.Y * M13 + vec.Z * M23 + vec.W * M33
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public ThisMatrix Multiply(in ThisMatrix other)
  => new(
    M00 * other.M00 + M01 * other.M10 + M02 * other.M20 + M03 * other.M30,
    M00 * other.M01 + M01 * other.M11 + M02 * other.M21 + M03 * other.M31,
    M00 * other.M02 + M01 * other.M12 + M02 * other.M22 + M03 * other.M32,
    M00 * other.M03 + M01 * other.M13 + M02 * other.M23 + M03 * other.M33,

    M10 * other.M00 + M11 * other.M10 + M12 * other.M20 + M13 * other.M30,
    M10 * other.M01 + M11 * other.M11 + M12 * other.M21 + M13 * other.M31,
    M10 * other.M02 + M11 * other.M12 + M12 * other.M22 + M13 * other.M32,
    M10 * other.M03 + M11 * other.M13 + M12 * other.M23 + M13 * other.M33,

    M20 * other.M00 + M21 * other.M10 + M22 * other.M20 + M23 * other.M30,
    M20 * other.M01 + M21 * other.M11 + M22 * other.M21 + M23 * other.M31,
    M20 * other.M02 + M21 * other.M12 + M22 * other.M22 + M23 * other.M32,
    M20 * other.M03 + M21 * other.M13 + M22 * other.M23 + M23 * other.M33,

    M30 * other.M00 + M31 * other.M10 + M32 * other.M20 + M33 * other.M30,
    M30 * other.M01 + M31 * other.M11 + M32 * other.M21 + M33 * other.M31,
    M30 * other.M02 + M31 * other.M12 + M32 * other.M22 + M33 * other.M32,
    M30 * other.M03 + M31 * other.M13 + M32 * other.M23 + M33 * other.M33
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Mat4x2F Multiply(in Mat4x2F right)
  => new(
    M00 * right.M00 + M01 * right.M10 + M02 * right.M20 + M03 * right.M30,
    M00 * right.M01 + M01 * right.M11 + M02 * right.M21 + M03 * right.M31,
    M10 * right.M00 + M11 * right.M10 + M12 * right.M20 + M13 * right.M30,
    M10 * right.M01 + M11 * right.M11 + M12 * right.M21 + M13 * right.M31,
    M20 * right.M00 + M21 * right.M10 + M22 * right.M20 + M23 * right.M30,
    M20 * right.M01 + M21 * right.M11 + M22 * right.M21 + M23 * right.M31,
    M30 * right.M00 + M31 * right.M10 + M32 * right.M20 + M33 * right.M30,
    M30 * right.M01 + M31 * right.M11 + M32 * right.M21 + M33 * right.M31
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Mat4x3F Multiply(in Mat4x3F right)
  => new(
    M00 * right.M00 + M01 * right.M10 + M02 * right.M20 + M03 * right.M30,
    M00 * right.M01 + M01 * right.M11 + M02 * right.M21 + M03 * right.M31,
    M00 * right.M02 + M01 * right.M12 + M02 * right.M22 + M03 * right.M32,
    M10 * right.M00 + M11 * right.M10 + M12 * right.M20 + M13 * right.M30,
    M10 * right.M01 + M11 * right.M11 + M12 * right.M21 + M13 * right.M31,
    M10 * right.M02 + M11 * right.M12 + M12 * right.M22 + M13 * right.M32,
    M20 * right.M00 + M21 * right.M10 + M22 * right.M20 + M23 * right.M30,
    M20 * right.M01 + M21 * right.M11 + M22 * right.M21 + M23 * right.M31,
    M20 * right.M02 + M21 * right.M12 + M22 * right.M22 + M23 * right.M32,
    M30 * right.M00 + M31 * right.M10 + M32 * right.M20 + M33 * right.M30,
    M30 * right.M01 + M31 * right.M11 + M32 * right.M21 + M33 * right.M31,
    M30 * right.M02 + M31 * right.M12 + M32 * right.M22 + M33 * right.M32
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
  public static Mat4x2F operator *(in ThisMatrix left, in Mat4x2F right)
  => left.Multiply(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Mat4x3F operator *(in ThisMatrix left, in Mat4x3F right)
  => left.Multiply(right);



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static implicit operator ThisMatrix(Mat4<ThisValue> other)
  => new(
    other.M00, other.M01, other.M02, other.M03,
    other.M10, other.M11, other.M12, other.M13,
    other.M20, other.M21, other.M22, other.M23,
    other.M30, other.M31, other.M32, other.M33
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static implicit operator Mat4<ThisValue>(ThisMatrix other)
  => new(
    other.M00, other.M01, other.M02, other.M03,
    other.M10, other.M11, other.M12, other.M13,
    other.M20, other.M21, other.M22, other.M23,
    other.M30, other.M31, other.M32, other.M33
  );



  public override string ToString() => Matrix.ToString<ThisMatrix, ThisValue>(this);
}
