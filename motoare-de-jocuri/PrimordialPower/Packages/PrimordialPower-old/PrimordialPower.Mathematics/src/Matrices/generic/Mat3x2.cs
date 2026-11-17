using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PrimordialPower.Mathematics.Vectors;
using PrimordialPower.Mathematics.ConstArrays;
using System.Numerics;

namespace PrimordialPower.Mathematics.Matrices;


[StructLayout(LayoutKind.Sequential)]
public struct Mat3x2<T> :
  IMatrix<Mat3x2<T>, T>,
  IMatrixTransposable<Mat3x2<T>, Mat2x3<T>>,
  IMatrixHasMatrixProduct<Mat3x2<T>, Mat2<T>, Mat3x2<T>>,
  IMatrixHasMatrixProduct<Mat3x2<T>, Mat2x3<T>, Mat3<T>>,
  IMatrixHasMatrixProduct<Mat3x2<T>, Mat2x4<T>, Mat3x4<T>>,
  IMatrixVectorComponents<Mat3x2<T>, Vec3<T>, Vec2<T>>
where T : INumber<T>, new()
{
  private ConstArray3<ConstArray2<T>> array;



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



  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Mat3x2(T m00, T m01, T m10, T m11, T m20, T m21)
  {
    M00 = m00; M01 = m01;
    M10 = m10; M11 = m11;
    M20 = m20; M21 = m21;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Mat3x2(T diagonal)
  {
    M00 = diagonal; M01 = T.Zero;
    M10 = T.Zero; M11 = diagonal;
    M20 = T.Zero; M21 = T.Zero;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Mat3x2(in Mat3x2<T> other)
  {
    this = other;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Mat3x2(in Vec2<T> r0, in Vec2<T> r1, in Vec2<T> r2)
  {
    this.R0 = r0;
    this.R1 = r1;
    this.R2 = r2;
  }



  public static uint RowCount => Vec3<T>.DimCount;
  public uint Rows => RowCount;

  public static uint ColumnCount => Vec2<T>.DimCount;
  public uint Columns => ColumnCount;

  public static uint ElementCount => 6;
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

  public Vec2<T> this[uint row]
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => GetRow(row);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => SetRow(row, value);
  }

  public Vec2<T> this[int row]
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => GetRow(row);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => SetRow(row, value);
  }



  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Vec2<T> GetRow(int row)
  {
    return new(this.array[row][0], this.array[row][1]);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Vec2<T> GetRow(uint row) => GetRow((int)row);

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void SetRow(int row, in Vec2<T> vec)
  {
    this.array[row][0] = vec.X;
    this.array[row][1] = vec.Y;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void SetRow(uint row, in Vec2<T> vec) => SetRow((int)row, vec);



  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Vec3<T> GetColumn(int col)
  {
    return new(this.array[0][col], this.array[1][col], this.array[2][col]);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Vec3<T> GetColumn(uint col) => GetColumn((int)col);

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void SetColumn(int col, in Vec3<T> vec)
  {
    this.array[0][col] = vec.X;
    this.array[1][col] = vec.Y;
    this.array[2][col] = vec.Z;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void SetColumn(uint col, in Vec3<T> vec) => SetColumn((int)col, vec);



  public Vec2<T> R0
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => GetRow(0);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => SetRow(0, value);
  }

  public Vec2<T> R1
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => GetRow(1);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => SetRow(1, value);
  }

  public Vec2<T> R2
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => GetRow(2);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => SetRow(2, value);
  }

  public Vec3<T> C0
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => GetColumn(0);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => SetColumn(0, value);
  }

  public Vec3<T> C1
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => GetColumn(1);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => SetColumn(1, value);
  }



  public Mat2x3<T> Transposed
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    get => new(
      M00, M10, M20,
      M01, M11, M21
    );
  }



  public static Mat3x2<T> Identity
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => new(T.One);
  }

  public static Mat3x2<T> Zero
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => new(T.Zero);
  }



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Mat3x2<T> Neg()
  => new(
    -M00, -M01,
    -M10, -M11,
    -M20, -M21
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Mat3x2<T> Add(in Mat3x2<T> other)
  => new(
    M00 + other.M00, M01 + other.M01,
    M10 + other.M10, M11 + other.M11,
    M20 + other.M20, M21 + other.M21
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Mat3x2<T> Add(T scalar)
  => new(
    M00 + scalar, M01 + scalar,
    M10 + scalar, M11 + scalar,
    M20 + scalar, M21 + scalar
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Mat3x2<T> Subtract(in Mat3x2<T> other)
  => new(
    M00 - other.M00, M01 - other.M01,
    M10 - other.M10, M11 - other.M11,
    M20 - other.M20, M21 - other.M21
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Mat3x2<T> Subtract(T scalar)
  => new(
    M00 - scalar, M01 - scalar,
    M10 - scalar, M11 - scalar,
    M20 - scalar, M21 - scalar
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Mat3x2<T> CompMultiply(in Mat3x2<T> other)
  => new(
    M00 * other.M00, M01 * other.M01,
    M10 * other.M10, M11 * other.M11,
    M20 * other.M20, M21 * other.M21
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Mat3x2<T> CompMultiply(T scalar)
  => new(
    M00 * scalar, M01 * scalar,
    M10 * scalar, M11 * scalar,
    M20 * scalar, M21 * scalar
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Mat3x2<T> CompDivide(in Mat3x2<T> other)
  => new(
    M00 / other.M00, M01 / other.M01,
    M10 / other.M10, M11 / other.M11,
    M20 / other.M20, M21 / other.M21
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Mat3x2<T> CompDivide(T scalar)
  => new(
    M00 / scalar, M01 / scalar,
    M10 / scalar, M11 / scalar,
    M20 / scalar, M21 / scalar
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Mat3x2<T> Modulo(in Mat3x2<T> other)
  => new(
    M00 % other.M00, M01 % other.M01,
    M10 % other.M10, M11 % other.M11,
    M20 % other.M20, M21 % other.M21
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Vec3<T> Multiply(in Vec2<T> vec)
  => new(
    M00 * vec.X + M01 * vec.Y,
    M10 * vec.X + M11 * vec.Y,
    M20 * vec.X + M21 * vec.Y
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Vec2<T> LeftMultiply(in Vec3<T> vec)
  => new(
    vec.X * M00 + vec.Y * M10 + vec.Z * M20,
    vec.X * M01 + vec.Y * M11 + vec.Z * M21
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Mat3x2<T> Multiply(in Mat2<T> other)
  => new(
    M00 * other.M00 + M01 * other.M10,
    M00 * other.M01 + M01 * other.M11,
    M10 * other.M00 + M11 * other.M10,
    M10 * other.M01 + M11 * other.M11,
    M20 * other.M00 + M21 * other.M10,
    M20 * other.M01 + M21 * other.M11
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Mat3<T> Multiply(in Mat2x3<T> other)
  => new(
    M00 * other.M00 + M01 * other.M10,
    M00 * other.M01 + M01 * other.M11,
    M00 * other.M02 + M01 * other.M12,
    M10 * other.M00 + M11 * other.M10,
    M10 * other.M01 + M11 * other.M11,
    M10 * other.M02 + M11 * other.M12,
    M20 * other.M00 + M21 * other.M10,
    M20 * other.M01 + M21 * other.M11,
    M20 * other.M02 + M21 * other.M12
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Mat3x4<T> Multiply(in Mat2x4<T> other)
  => new(
    M00 * other.M00 + M01 * other.M10,
    M00 * other.M01 + M01 * other.M11,
    M00 * other.M02 + M01 * other.M12,
    M00 * other.M03 + M01 * other.M13,
    M10 * other.M00 + M11 * other.M10,
    M10 * other.M01 + M11 * other.M11,
    M10 * other.M02 + M11 * other.M12,
    M10 * other.M03 + M11 * other.M13,
    M20 * other.M00 + M21 * other.M10,
    M20 * other.M01 + M21 * other.M11,
    M20 * other.M02 + M21 * other.M12,
    M20 * other.M03 + M21 * other.M13
  );



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Mat3x2<T> operator -(in Mat3x2<T> right)
  => right.Neg();



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Mat3x2<T> operator +(in Mat3x2<T> left, in Mat3x2<T> right)
  => left.Add(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Mat3x2<T> operator +(in Mat3x2<T> left, T right)
  => left.Add(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Mat3x2<T> operator +(T left, in Mat3x2<T> right)
  => right.Add(left);



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Mat3x2<T> operator -(in Mat3x2<T> left, in Mat3x2<T> right)
  => left.Subtract(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Mat3x2<T> operator -(in Mat3x2<T> left, T right)
  => left.Subtract(right);



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Mat3x2<T> operator *(in Mat3x2<T> left, T right)
  => left.CompMultiply(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Mat3x2<T> operator *(T left, in Mat3x2<T> right)
  => right.CompMultiply(left);



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Mat3x2<T> operator /(in Mat3x2<T> left, T right)
  => left.CompDivide(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Mat3x2<T> operator %(in Mat3x2<T> left, in Mat3x2<T> right)
  => left.Modulo(right);



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec2<T> operator *(in Vec3<T> left, in Mat3x2<T> right)
  => right.LeftMultiply(left);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec3<T> operator *(in Mat3x2<T> left, in Vec2<T> right)
  => left.Multiply(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Mat3x2<T> operator *(in Mat3x2<T> left, in Mat2<T> right)
  => left.Multiply(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Mat3<T> operator *(in Mat3x2<T> left, in Mat2x3<T> right)
  => left.Multiply(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Mat3x4<T> operator *(in Mat3x2<T> left, in Mat2x4<T> right)
  => left.Multiply(right);



  public override string ToString() => Matrix.ToString<Mat3x2<T>, T>(this);
}
