using System.Runtime.CompilerServices;

namespace PrimordialPower.Mathematics.ConstArrays;


[InlineArray(16)]
public struct ConstArray16<T>()
where T : new()
{
  private T value = new();
}