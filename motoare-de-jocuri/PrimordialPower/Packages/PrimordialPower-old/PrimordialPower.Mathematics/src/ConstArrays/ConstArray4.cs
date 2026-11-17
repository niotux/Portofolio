using System.Runtime.CompilerServices;

namespace PrimordialPower.Mathematics.ConstArrays;


[InlineArray(4)]
public struct ConstArray4<T>()
where T : new()
{
  private T value = new();
}