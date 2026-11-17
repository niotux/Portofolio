using System.Runtime.CompilerServices;

namespace PrimordialPower.Mathematics.ConstArrays;


[InlineArray(5)]
public struct ConstArray5<T>()
where T : new()
{
  private T value = new();
}