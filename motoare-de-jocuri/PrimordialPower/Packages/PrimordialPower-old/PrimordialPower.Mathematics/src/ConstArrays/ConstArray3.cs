using System.Runtime.CompilerServices;

namespace PrimordialPower.Mathematics.ConstArrays;


[InlineArray(3)]
public struct ConstArray3<T>()
where T : new()
{
  private T value = new();
}