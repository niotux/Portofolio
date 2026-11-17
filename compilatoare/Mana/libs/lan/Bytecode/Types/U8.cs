namespace LAN.Bytecode.Types;

using System.Buffers.Binary;
using ThisType = U8;
using WrappingType = byte;
public readonly record struct U8
(
  WrappingType Value
)
: IBytecodeReadableType<ThisType>, IBytecodeWriteableType<ThisType>
{
  public static ThisType Read(Stream dest)
    => (byte)dest.ReadByte();

  public static void Write(Stream dest, ThisType value)
    => dest.WriteByte(value.Value);

  public static implicit operator ThisType(WrappingType value)
  {
    return new(value);
  }
  public static implicit operator WrappingType(ThisType value)
  {
    return value.Value;
  }
}