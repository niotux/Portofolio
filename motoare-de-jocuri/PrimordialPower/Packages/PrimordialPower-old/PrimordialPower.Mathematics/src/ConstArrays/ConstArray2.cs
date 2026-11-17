using System.Runtime.CompilerServices;

namespace PrimordialPower.Mathematics.ConstArrays;


[InlineArray(2)]
public struct ConstArray2<T>()
where T : new()
{
  private T value = new();
}