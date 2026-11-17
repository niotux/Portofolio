using System.Runtime.CompilerServices;

namespace PrimordialPower.Mathematics.ConstArrays;


[InlineArray(12)]
public struct ConstArray12<T>()
where T : new()
{
  private T value = new();
}