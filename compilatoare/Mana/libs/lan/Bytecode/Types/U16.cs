namespace LAN.Bytecode.Types;

using System.Buffers.Binary;
using ThisType = U16;
using WrappingType = ushort;
public readonly record struct U16
(
  WrappingType Value
)
: IBytecodeReadableType<ThisType>, IBytecodeWriteableType<ThisType>
{
  public static ThisType Read(Stream dest)
  {
    Span<byte> bytes = stackalloc byte[sizeof(WrappingType)];
    return BinaryPrimitives.ReadUInt16LittleEndian(bytes);
  }

  public static void Write(Stream dest, ThisType value)
  {
    Span<byte> bytes = stackalloc byte[sizeof(WrappingType)];
    BinaryPrimitives.WriteUInt16LittleEndian(bytes, value.Value);
    dest.Write(bytes);
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