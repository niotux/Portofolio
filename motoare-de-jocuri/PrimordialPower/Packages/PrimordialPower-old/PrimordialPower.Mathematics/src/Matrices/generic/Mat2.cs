using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PrimordialPower.Mathematics.Vectors;
using PrimordialPower.Mathematics.ConstArrays;
using System.Numerics;

namespace PrimordialPower.Mathematics.Matrices;


[StructLayout(LayoutKind.Sequential)]
public struct Mat2<T> :
  IMatrixSquare<Mat2<T>, T>,
  IMatrixHasMatrixProduct<Mat2<T>, Mat2x3<T>, Mat2x3<T>>,
  IMatrixHasMatrixProduct<Mat2<T>, Mat2x4<T>, Mat2x4<T>>,
  IMatrixVectorComponents<Mat2<T>, Vec2<T>, Vec2<T>>
where T : INumber<T>, new()
{
  private ConstArray2<ConstArray2<T>> array;



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



  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Mat2(T m00, T m01, T m10, T m11)
  {
    M00 = m00; M01 = m01;
    M10 = m10; M11 = m11;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Mat2(T diagonal)
  {
    M00 = diagonal; M01 = T.Zero;
    M10 = T.Zero; M11 = diagonal;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Mat2(in Mat2<T> other)
  {
    this = other;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Mat2(in Vec2<T> r0, in Vec2<T> r1)
  {
    this.R0 = r0;
    this.R1 = r1;
  }



  public static uint DimCount => Vec2<T>.DimCount;
  public uint Dimension => DimCount;

  public static uint RowCount => Vec2<T>.DimCount;
  public uint Rows => RowCount;

  public static uint ColumnCount => Vec2<T>.DimCount;
  public uint Columns => ColumnCount;

  public static uint ElementCount => 4;
  public uint Elements => ElementCount;



  public T this[int row, int col]
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => this.array[row][col];
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => this.array[row][col] = value;
  }

  public T this[uint row, uint col]
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => this[(int)row, (int)col];
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
  public Vec2<T> GetColumn(int col)
  {
    return new(this.array[0][col], this.array[1][col]);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Vec2<T> GetColumn(uint row) => GetColumn((int)row);

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void SetColumn(int col, in Vec2<T> vec)
  {
    this.array[col][0] = vec.X;
    this.array[col][1] = vec.Y;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void SetColumn(uint col, in Vec2<T> vec) => SetRow((int)col, vec);



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

  public Vec2<T> C0
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => GetColumn(0);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => SetColumn(0, value);
  }

  public Vec2<T> C1
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => GetColumn(1);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => SetColumn(1, value);
  }



  public T Determinant
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    get => M00 * M11 - M01 * M10;
  }

  public T Trace
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    get => M00 + M11;
  }



  public Mat2<T> Transposed
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



  public static Mat2<T> Identity
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => new(T.One);
  }

  public static Mat2<T> Zero
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => new(T.Zero);
  }



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Mat2<T> Neg()
  => new(
    -M00, -M01,
    -M10, -M11
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Mat2<T> Add(in Mat2<T> other)
  => new(
    M00 + other.M00, M01 + other.M01,
    M10 + other.M10, M11 + other.M11
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Mat2<T> Add(T scalar)
  => new(
    M00 + scalar, M01 + scalar,
    M10 + scalar, M11 + scalar
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Mat2<T> Subtract(in Mat2<T> other)
  => new(
    M00 - other.M00, M01 - other.M01,
    M10 - other.M10, M11 - other.M11
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Mat2<T> Subtract(T scalar)
  => new(
    M00 - scalar, M01 - scalar,
    M10 - scalar, M11 - scalar
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Mat2<T> CompMultiply(in Mat2<T> other)
  => new(
    M00 * other.M00, M01 * other.M01,
    M10 * other.M10, M11 * other.M11
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Mat2<T> CompMultiply(T scalar)
  => new(
    M00 * scalar, M01 * scalar,
    M10 * scalar, M11 * scalar
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Mat2<T> CompDivide(in Mat2<T> other)
  => new(
    M00 / other.M00, M01 / other.M01,
    M10 / other.M10, M11 / other.M11
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Mat2<T> CompDivide(T scalar)
  => new(
    M00 / scalar, M01 / scalar,
    M10 / scalar, M11 / scalar
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Mat2<T> Modulo(in Mat2<T> other)
  => new(
    M00 % other.M00, M01 % other.M01,
    M10 % other.M10, M11 % other.M11
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Mat2<T> Multiply(in Mat2<T> other)
  => new(
    M00 * other.M00 + M01 * other.M10,
    M00 * other.M01 + M01 * other.M11,

    M10 * other.M00 + M11 * other.M10,
    M10 * other.M01 + M11 * other.M11
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Vec2<T> Multiply(in Vec2<T> vec)
  => new(
    M00 * vec.X + M01 * vec.Y,
    M10 * vec.X + M11 * vec.Y
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Vec2<T> LeftMultiply(in Vec2<T> vec)
  => new(
    vec.X * M00 + vec.Y * M10,
    vec.X * M01 + vec.Y * M11
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Mat2x3<T> Multiply(in Mat2x3<T> other)
  => new(
    M00 * other.M00 + M01 * other.M10,
    M00 * other.M01 + M01 * other.M11,
    M00 * other.M02 + M01 * other.M12,
    M10 * other.M00 + M11 * other.M10,
    M10 * other.M01 + M11 * other.M11,
    M10 * other.M02 + M11 * other.M12
  );

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public Mat2x4<T> Multiply(in Mat2x4<T> other)
  => new(
    M00 * other.M00 + M01 * other.M10,
    M00 * other.M01 + M01 * other.M11,
    M00 * other.M02 + M01 * other.M12,
    M00 * other.M03 + M01 * other.M13,
    M10 * other.M00 + M11 * other.M10,
    M10 * other.M01 + M11 * other.M11,
    M10 * other.M02 + M11 * other.M12,
    M10 * other.M03 + M11 * other.M13
  );



  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Mat2<T> operator -(in Mat2<T> right)
  => right.Neg();

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Mat2<T> operator +(in Mat2<T> left, in Mat2<T> right)
  => left.Add(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Mat2<T> operator +(in Mat2<T> left, T right)
  => left.Add(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Mat2<T> operator +(T left, in Mat2<T> right)
  => right.Add(left);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Mat2<T> operator -(in Mat2<T> left, in Mat2<T> right)
  => left.Subtract(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Mat2<T> operator -(in Mat2<T> left, T right)
  => left.Subtract(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Mat2<T> operator %(in Mat2<T> left, in Mat2<T> right)
  => left.Modulo(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Mat2<T> operator *(in Mat2<T> left, T right)
  => left.CompMultiply(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Mat2<T> operator *(T left, in Mat2<T> right)
  => right.CompMultiply(left);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Mat2<T> operator /(in Mat2<T> left, T right)
  => left.CompDivide(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Mat2<T> operator *(in Mat2<T> left, in Mat2<T> right)
  => left.Multiply(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec2<T> operator *(in Vec2<T> left, in Mat2<T> right)
  => right.LeftMultiply(left);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Vec2<T> operator *(in Mat2<T> left, in Vec2<T> right)
  => left.Multiply(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Mat2x3<T> operator *(in Mat2<T> left, in Mat2x3<T> right)
  => left.Multiply(right);

  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
  public static Mat2x4<T> operator *(in Mat2<T> left, in Mat2x4<T> right)
  => left.Multiply(right);



  public override string ToString() => Matrix.ToString<Mat2<T>, T>(this);
}
