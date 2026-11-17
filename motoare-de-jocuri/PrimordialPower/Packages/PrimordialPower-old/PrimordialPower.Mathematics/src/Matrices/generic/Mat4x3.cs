using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PrimordialPower.Mathematics.Vectors;
using PrimordialPower.Mathematics.ConstArrays;
using System.Numerics;

namespace PrimordialPower.Mathematics.Matrices;


[StructLayout(LayoutKind.Sequential)]
public struct Mat4x3<T> :
  IMatrix<Mat4x3<T>, T>,
  IMatrixTransposable<Mat4x3<T>, Mat3x4<T>>,
  IMatrixHasMatrixProduct<Mat4x3<T>, Mat3x2<T>, Mat4x2<T>>,
  IMatrixHasMatrixProduct<Mat4x3<T>, Mat3<T>, Mat4x3<T>>,
  IMatrixHasMatrixProduct<Mat4x3<T>, Mat3x4<T>, Mat4<T>>,
  IMatrixVectorComponents<Mat4x3<T>, Vec4<T>, Vec3<T>>
where T : INumber<T>, new()
{
  private ConstArray4<ConstArray3<T>> array;



  public T M00
  {
    get => this.array[0][0];
    set => this.array[0][0] = value;
  }

  public T M01
  {
    get => this.array[0][1];
    set => this.array[0][1] = value;
  }

  public T M02
  {
    get => this.array[0][2];
    set => this.array[0][2] = value;
  }

  public T M10
  {
    get => this.array[1][0];
    set => this.array[1][0] = value;
  }

  public T M11
  {
    get => this.array[1][1];
    set => this.array[1][1] = value;
  }

  public T M12
  {
    get => this.array[1][2];
    set => this.array[1][2] = value;
  }

  public T M20
  {
    get => this.array[2][0];
    set => this.array[2][0] = value;
  }

  public T M21
  {
    get => this.array[2][1];
    set => this.array[2][1] = value;
  }

  public T M22
  {
    get => this.array[2][2];
    set => this.array[2][2] = value;
  }

  public T M30
  {
    get => this.array[3][0];
    set => this.array[3][0] = value;
  }

  public T M31
  {
    get => this.array[3][1];
    set => this.array[3][1] = value;
  }

  public T M32
  {
    get => this.array[3][2];
    set => this.array[3][2] = value;
  }



  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Mat4x3(T m00, T m01, T m02, T m10, T m11, T m12, T m20, T m21, T m22, T m30, T m31, T m32)
  {
    M00 = m00; M01 = m01; M02 = m02;
    M10 = m10; M11 = m11; M12 = m12;
    M20 = m20; M21 = m21; M22 = m22;
    M30 = m30; M31 = m31; M32 = m32;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Mat4x3(T diagonal)
  {
    M00 = diagonal; M01 = T.Zero; M02 = T.Zero;
    M10 = T.Zero; M11 = diagonal; M12 = T.Zero;
    M20 = T.Zero; M21 = T.Zero; M22 = diagonal;
    M30 = T.Zero; M31 = T.Zero; M32 = T.Zero;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Mat4x3(in Mat4x3<T> other)
  {
    this = other;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Mat4x3(in Vec3<T> r0, in Vec3<T> r1, in Vec3<T> r2, in Vec3<T> r3)
  {
    this.R0 = r0;
    this.R1 = r1;
    this.R2 = r2;
    this.R3 = r3;
  }



  public static uint RowCount => Vec4<T>.DimCount;
  public uint Rows => RowCount;

  public static uint ColumnCount => Vec3<T>.DimCount;
  public uint Columns => ColumnCount;

  public static uint ElementCount => 12;
  public uint Elements => ElementCount;



  public T this[int row, int col]
  {
    get => this.array[row][col];
    set => this.array[row][col] = value;
  }

  public T this[uint row, uint col]
  {
    get => this[(int)row, (int)col];
    set => this[(int)row, (int)col] = value;
  }

  public Vec3<T> this[uint row]
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => GetRow(row);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => SetRow(row, value);
  }

  public Vec3<T> this[int row]
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => GetRow(row);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => SetRow(row, value);
  }



  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Vec3<T> GetRow(int row)
  {
    return new(this.array[row][0], this.array[row][1], this.array[row][2]);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Vec3<T> GetRow(uint row) => GetRow((int)row);

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void SetRow(int row, in Vec3<T> vec)
  {
    this.array[row][0] = vec.X;
    this.array[row][1] = vec.Y;
    this.array[row][2] = vec.Z;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void SetRow(uint row, in Vec3<T> vec) => SetRow((int)row, vec);



  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Vec4<T> GetColumn(int col)
  {
    return new(this.array[0][col], this.array[1][col], this.array[2][col], this.array[3][col]);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Vec4<T> GetColumn(uint col) => GetColumn((int)col);

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void SetColumn(int col, in Vec4<T> vec)
  {
    this.array[0][col] = vec.X;
    this.array[1][col] = vec.Y;
    this.array[2][col] = vec.Z;
    this.array[3][col] = vec.W;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void SetColumn(uint col, in Vec4<T> vec) => SetColumn((int)col, vec);



  public Vec3<T> R0
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => GetRow(0);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => SetRow(0, value);
  }

  public Vec3<T> R1
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => GetRow(1);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => SetRow(1, value);
  }

  public Vec3<T> R2
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => GetRow(2);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => SetRow(2, value);
  }

  public Vec3<T> R3
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => GetRow(3);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => SetRow(3, value);
  }

  public Vec4<T> C0
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => GetColumn(0);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => SetColumn(0, value);
  }

  public Vec4<T> C1
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => GetColumn(1);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => SetColumn(1, value);
  }

  public Vec4<T> C2
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => GetColumn(2);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => SetColumn(2, value);
  }



  public Mat3x4<T> Transposed
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    get => new(
      M00, M10, M20, M30,
      M01, M11, M21, M31,
      M02, M12, M22, M32
    );
  }



  public static Mat4x3<T> Identity
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => new(T.One);
  }

  public static Mat4x3<T> Zero
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => new(T.Zero);
  }



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Mat4x3<T> Neg()
  => new(
    -M00, -M01, -M02,
    -M10, -M11, -M12,
    -M20, -M21, -M22,
    -M30, -M31, -M32
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Mat4x3<T> Add(in Mat4x3<T> other)
  => new(
    M00 + other.M00, M01 + other.M01, M02 + other.M02,
    M10 + other.M10, M11 + other.M11, M12 + other.M12,
    M20 + other.M20, M21 + other.M21, M22 + other.M22,
    M30 + other.M30, M31 + other.M31, M32 + other.M32
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Mat4x3<T> Add(T scalar)
  => new(
    M00 + scalar, M01 + scalar, M02 + scalar,
    M10 + scalar, M11 + scalar, M12 + scalar,
    M20 + scalar, M21 + scalar, M22 + scalar,
    M30 + scalar, M31 + scalar, M32 + scalar
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Mat4x3<T> Subtract(in Mat4x3<T> other)
  => new(
    M00 - other.M00, M01 - other.M01, M02 - other.M02,
    M10 - other.M10, M11 - other.M11, M12 - other.M12,
    M20 - other.M20, M21 - other.M21, M22 - other.M22,
    M30 - other.M30, M31 - other.M31, M32 - other.M32
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Mat4x3<T> Subtract(T scalar)
  => new(
    M00 - scalar, M01 - scalar, M02 - scalar,
    M10 - scalar, M11 - scalar, M12 - scalar,
    M20 - scalar, M21 - scalar, M22 - scalar,
    M30 - scalar, M31 - scalar, M32 - scalar
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Mat4x3<T> CompMultiply(in Mat4x3<T> other)
  => new(
    M00 * other.M00, M01 * other.M01, M02 * other.M02,
    M10 * other.M10, M11 * other.M11, M12 * other.M12,
    M20 * other.M20, M21 * other.M21, M22 * other.M22,
    M30 * other.M30, M31 * other.M31, M32 * other.M32
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Mat4x3<T> CompMultiply(T scalar)
  => new(
    M00 * scalar, M01 * scalar, M02 * scalar,
    M10 * scalar, M11 * scalar, M12 * scalar,
    M20 * scalar, M21 * scalar, M22 * scalar,
    M30 * scalar, M31 * scalar, M32 * scalar
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Mat4x3<T> CompDivide(in Mat4x3<T> other)
  => new(
    M00 / other.M00, M01 / other.M01, M02 / other.M02,
    M10 / other.M10, M11 / other.M11, M12 / other.M12,
    M20 / other.M20, M21 / other.M21, M22 / other.M22,
    M30 / other.M30, M31 / other.M31, M32 / other.M32
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Mat4x3<T> CompDivide(T scalar)
  => new(
    M00 / scalar, M01 / scalar, M02 / scalar,
    M10 / scalar, M11 / scalar, M12 / scalar,
    M20 / scalar, M21 / scalar, M22 / scalar,
    M30 / scalar, M31 / scalar, M32 / scalar
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Mat4x3<T> Modulo(in Mat4x3<T> other)
  => new(
    M00 % other.M00, M01 % other.M01, M02 % other.M02,
    M10 % other.M10, M11 % other.M11, M12 % other.M12,
    M20 % other.M20, M21 % other.M21, M22 % other.M22,
    M30 % other.M30, M31 % other.M31, M32 % other.M32
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Vec4<T> Multiply(in Vec3<T> vec)
  => new(
    M00 * vec.X + M01 * vec.Y + M02 * vec.Z,
    M10 * vec.X + M11 * vec.Y + M12 * vec.Z,
    M20 * vec.X + M21 * vec.Y + M22 * vec.Z,
    M30 * vec.X + M31 * vec.Y + M32 * vec.Z
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Vec3<T> LeftMultiply(in Vec4<T> vec)
  => new(
    vec.X * M00 + vec.Y * M10 + vec.Z * M20 + vec.W * M30,
    vec.X * M01 + vec.Y * M11 + vec.Z * M21 + vec.W * M31,
    vec.X * M02 + vec.Y * M12 + vec.Z * M22 + vec.W * M32
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Mat4x2<T> Multiply(in Mat3x2<T> other)
  => new(
    M00 * other.M00 + M01 * other.M10 + M02 * other.M20,
    M00 * other.M01 + M01 * other.M11 + M02 * other.M21,
    M10 * other.M00 + M11 * other.M10 + M12 * other.M20,
    M10 * other.M01 + M11 * other.M11 + M12 * other.M21,
    M20 * other.M00 + M21 * other.M10 + M22 * other.M20,
    M20 * other.M01 + M21 * other.M11 + M22 * other.M21,
    M30 * other.M00 + M31 * other.M10 + M32 * other.M20,
    M30 * other.M01 + M31 * other.M11 + M32 * other.M21
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Mat4x3<T> Multiply(in Mat3<T> other)
  => new(
    M00 * other.M00 + M01 * other.M10 + M02 * other.M20,
    M00 * other.M01 + M01 * other.M11 + M02 * other.M21,
    M00 * other.M02 + M01 * other.M12 + M02 * other.M22,
    M10 * other.M00 + M11 * other.M10 + M12 * other.M20,
    M10 * other.M01 + M11 * other.M11 + M12 * other.M21,
    M10 * other.M02 + M11 * other.M12 + M12 * other.M22,
    M20 * other.M00 + M21 * other.M10 + M22 * other.M20,
    M20 * other.M01 + M21 * other.M11 + M22 * other.M21,
    M20 * other.M02 + M21 * other.M12 + M22 * other.M22,
    M30 * other.M00 + M31 * other.M10 + M32 * other.M20,
    M30 * other.M01 + M31 * other.M11 + M32 * other.M21,
    M30 * other.M02 + M31 * other.M12 + M32 * other.M22
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Mat4<T> Multiply(in Mat3x4<T> other)
  => new(
    M00 * other.M00 + M01 * other.M10 + M02 * other.M20,
    M00 * other.M01 + M01 * other.M11 + M02 * other.M21,
    M00 * other.M02 + M01 * other.M12 + M02 * other.M22,
    M00 * other.M03 + M01 * other.M13 + M02 * other.M23,
    M10 * other.M00 + M11 * other.M10 + M12 * other.M20,
    M10 * other.M01 + M11 * other.M11 + M12 * other.M21,
    M10 * other.M02 + M11 * other.M12 + M12 * other.M22,
    M10 * other.M03 + M11 * other.M13 + M12 * other.M23,
    M20 * other.M00 + M21 * other.M10 + M22 * other.M20,
    M20 * other.M01 + M21 * other.M11 + M22 * other.M21,
    M20 * other.M02 + M21 * other.M12 + M22 * other.M22,
    M20 * other.M03 + M21 * other.M13 + M22 * other.M23,
    M30 * other.M00 + M31 * other.M10 + M32 * other.M20,
    M30 * other.M01 + M31 * other.M11 + M32 * other.M21,
    M30 * other.M02 + M31 * other.M12 + M32 * other.M22,
    M30 * other.M03 + M31 * other.M13 + M32 * other.M23
  );



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Mat4x3<T> operator -(in Mat4x3<T> right)
  => right.Neg();



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Mat4x3<T> operator +(in Mat4x3<T> left, in Mat4x3<T> right)
  => left.Add(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Mat4x3<T> operator +(in Mat4x3<T> left, T right)
  => left.Add(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Mat4x3<T> operator +(T left, in Mat4x3<T> right)
  => right.Add(left);



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Mat4x3<T> operator -(in Mat4x3<T> left, in Mat4x3<T> right)
  => left.Subtract(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Mat4x3<T> operator -(in Mat4x3<T> left, T right)
  => left.Subtract(right);



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Mat4x3<T> operator *(in Mat4x3<T> left, T right)
  => left.CompMultiply(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Mat4x3<T> operator *(T left, in Mat4x3<T> right)
  => right.CompMultiply(left);



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Mat4x3<T> operator /(in Mat4x3<T> left, T right)
  => left.CompDivide(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Mat4x3<T> operator %(in Mat4x3<T> left, in Mat4x3<T> right)
  => left.Modulo(right);



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec3<T> operator *(in Vec4<T> left, in Mat4x3<T> right)
  => new(
    left.X * right.M00 + left.Y * right.M10 + left.Z * right.M20 + left.W * right.M30,
    left.X * right.M01 + left.Y * right.M11 + left.Z * right.M21 + left.W * right.M31,
    left.X * right.M02 + left.Y * right.M12 + left.Z * right.M22 + left.W * right.M32
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec4<T> operator *(in Mat4x3<T> left, in Vec3<T> right)
  => new(
    left.M00 * right.X + left.M01 * right.Y + left.M02 * right.Z,
    left.M10 * right.X + left.M11 * right.Y + left.M12 * right.Z,
    left.M20 * right.X + left.M21 * right.Y + left.M22 * right.Z,
    left.M30 * right.X + left.M31 * right.Y + left.M32 * right.Z
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Mat4x2<T> operator *(in Mat4x3<T> left, in Mat3x2<T> right)
  => new(
    left.M00 * right.M00 + left.M01 * right.M10 + left.M02 * right.M20,
    left.M00 * right.M01 + left.M01 * right.M11 + left.M02 * right.M21,
    left.M10 * right.M00 + left.M11 * right.M10 + left.M12 * right.M20,
    left.M10 * right.M01 + left.M11 * right.M11 + left.M12 * right.M21,
    left.M20 * right.M00 + left.M21 * right.M10 + left.M22 * right.M20,
    left.M20 * right.M01 + left.M21 * right.M11 + left.M22 * right.M21,
    left.M30 * right.M00 + left.M31 * right.M10 + left.M32 * right.M20,
    left.M30 * right.M01 + left.M31 * right.M11 + left.M32 * right.M21
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Mat4x3<T> operator *(in Mat4x3<T> left, in Mat3<T> right)
  => new(
    left.M00 * right.M00 + left.M01 * right.M10 + left.M02 * right.M20,
    left.M00 * right.M01 + left.M01 * right.M11 + left.M02 * right.M21,
    left.M00 * right.M02 + left.M01 * right.M12 + left.M02 * right.M22,
    left.M10 * right.M00 + left.M11 * right.M10 + left.M12 * right.M20,
    left.M10 * right.M01 + left.M11 * right.M11 + left.M12 * right.M21,
    left.M10 * right.M02 + left.M11 * right.M12 + left.M12 * right.M22,
    left.M20 * right.M00 + left.M21 * right.M10 + left.M22 * right.M20,
    left.M20 * right.M01 + left.M21 * right.M11 + left.M22 * right.M21,
    left.M20 * right.M02 + left.M21 * right.M12 + left.M22 * right.M22,
    left.M30 * right.M00 + left.M31 * right.M10 + left.M32 * right.M20,
    left.M30 * right.M01 + left.M31 * right.M11 + left.M32 * right.M21,
    left.M30 * right.M02 + left.M31 * right.M12 + left.M32 * right.M22
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Mat4<T> operator *(in Mat4x3<T> left, in Mat3x4<T> right)
  => new(
    left.M00 * right.M00 + left.M01 * right.M10 + left.M02 * right.M20,
    left.M00 * right.M01 + left.M01 * right.M11 + left.M02 * right.M21,
    left.M00 * right.M02 + left.M01 * right.M12 + left.M02 * right.M22,
    left.M00 * right.M03 + left.M01 * right.M13 + left.M02 * right.M23,
    left.M10 * right.M00 + left.M11 * right.M10 + left.M12 * right.M20,
    left.M10 * right.M01 + left.M11 * right.M11 + left.M12 * right.M21,
    left.M10 * right.M02 + left.M11 * right.M12 + left.M12 * right.M22,
    left.M10 * right.M03 + left.M11 * right.M13 + left.M12 * right.M23,
    left.M20 * right.M00 + left.M21 * right.M10 + left.M22 * right.M20,
    left.M20 * right.M01 + left.M21 * right.M11 + left.M22 * right.M21,
    left.M20 * right.M02 + left.M21 * right.M12 + left.M22 * right.M22,
    left.M20 * right.M03 + left.M21 * right.M13 + left.M22 * right.M23,
    left.M30 * right.M00 + left.M31 * right.M10 + left.M32 * right.M20,
    left.M30 * right.M01 + left.M31 * right.M11 + left.M32 * right.M21,
    left.M30 * right.M02 + left.M31 * right.M12 + left.M32 * right.M22,
    left.M30 * right.M03 + left.M31 * right.M13 + left.M32 * right.M23
  );



  public override string ToString() => Matrix.ToString<Mat4x3<T>, T>(this);
}
