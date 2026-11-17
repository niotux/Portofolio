using System.Runtime.CompilerServices;

namespace PrimordialPower.Mathematics.ConstArrays;


[InlineArray(9)]
public struct ConstArray9<T>()
where T : new()
{
  private T value = new();
}