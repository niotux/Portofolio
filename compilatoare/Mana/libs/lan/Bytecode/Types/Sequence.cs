namespace LAN.Bytecode.Types;

using ThisType = Sequence;
using WrappingType = byte[];
public readonly record struct Sequence
(
  WrappingType Value
)
: IBytecodeReadableType<ThisType, ulong>, IBytecodeWriteableType<ThisType>
{
  public static ThisType Read(Stream dest, ulong size)
  {
    var memory = new byte[size];
    _ = dest.Read(memory);
    return memory;
  }

  public static void Write(Stream dest, ThisType value)
  {
    dest.Write(value.Value);
  }

  public static implicit operator ThisType(WrappingType value)
  {
    return new(value);
  }
  public static implicit operator WrappingType(ThisType value)
  {
    return value.Value;
  }
}