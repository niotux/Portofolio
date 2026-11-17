// using System.Numerics;
// using System.Runtime.CompilerServices;
// using System.Runtime.InteropServices;
// using System.Runtime.Intrinsics;

// namespace OpenUL.Mathematics.Vectors;


// [StructLayout(LayoutKind.Sequential)]
// public record struct Vec2<T> : IVector2<Vec2<T>, T>
//   where T : unmanaged, INumber<T>
// {
//   public T X;
//   public T Y;


//   public static Vec2<T> Create(T x, T y) => new(x, y);
//   public static Vec2<T> Create(T value) => new(value, value);
//   public Vec2(T x, T y)
//   {
//     X = x;
//     Y = y;
//   }


//   T IVector2<Vec2<T>, T>.X { readonly get => X; set => X = value; }
//   T IVector2<Vec2<T>, T>.Y { readonly get => Y; set => Y = value; }

//   public T this[int index]
//   {
//     readonly get => index switch
//     {
//       0 => X,
//       1 => Y,
//       _ => throw new IndexOutOfRangeException(),
//     };
//     set
//     {
//       switch (index)
//       {
//         case 0:
//           X = value;
//           break;
//         case 1:
//           Y = value;
//           break;

//         default: throw new IndexOutOfRangeException();
//       }
//     }
//   }

//   public unsafe static TNum Dot<TNum>(Vec2<T> left, Vec2<T> right, NumberCreation creation = NumberCreation.Saturate)
//     where TNum : System.Numerics.INumber<TNum>
//   {
//     return sizeof(Vec2<T>) switch
//     {
//       4 => Number.Convert<T, TNum>(
//         Vector64.Dot(
//           Unsafe.As<Vec2<T>, Vector64<T>>(ref left),
//           Unsafe.As<Vec2<T>, Vector64<T>>(ref right)
//         )
//       ),
//       8 => Number.Convert<T, TNum>(
//         Vector128.Dot(
//           Unsafe.As<Vec2<T>, Vector128<T>>(ref left),
//           Unsafe.As<Vec2<T>, Vector128<T>>(ref right)
//         )
//       ),
//       16 => Number.Convert<T, TNum>(
//         Vector256.Dot(
//           Unsafe.As<Vec2<T>, Vector256<T>>(ref left),
//           Unsafe.As<Vec2<T>, Vector256<T>>(ref right)
//         )
//       ),
//       32 => Number.Convert<T, TNum>(
//         Vector512.Dot(
//           Unsafe.As<Vec2<T>, Vector512<T>>(ref left),
//           Unsafe.As<Vec2<T>, Vector512<T>>(ref right)
//         )
//       ),
//     };
//   }

//   public static Vec2<T> operator +(Vec2<T> left, Vec2<T> right)
//   {
//     return new Vec2<T>() with { Vector = left.Vector + right.Vector };
//   }

//   public static Vec2<T> operator -(Vec2<T> left, Vec2<T> right)
//   {
//     return new Vec2<T>() with { Vector = left.Vector - right.Vector };
//   }

//   public static Vec2<T> operator *(Vec2<T> left, Vec2<T> right)
//   {
//     return new Vec2<T>() with { Vector = left.Vector * right.Vector };
//   }

//   public static Vec2<T> operator /(Vec2<T> left, Vec2<T> right)
//   {
//     return new Vec2<T>() with { Vector = left.Vector / right.Vector };
//   }

//   public static Vec2<T> operator +(Vec2<T> value)
//   {
//     return value;
//   }

//   public static Vec2<T> operator -(Vec2<T> value)
//   {
//     return new Vec2<T>() with { Vector = -value.Vector };
//   }
// }