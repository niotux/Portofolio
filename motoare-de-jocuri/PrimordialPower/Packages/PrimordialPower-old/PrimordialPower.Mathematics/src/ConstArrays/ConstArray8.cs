using System.Runtime.CompilerServices;

namespace PrimordialPower.Mathematics.ConstArrays;


[InlineArray(8)]
public struct ConstArray8<T>()
where T : new()
{
  private T value = new();
}