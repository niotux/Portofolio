namespace LAN.Bytecode.Types;

using System.Buffers.Binary;
using ThisType = U64;
using WrappingType = ulong;
public readonly record struct U64
(
  WrappingType Value
)
: IBytecodeReadableType<ThisType>, IBytecodeWriteableType<ThisType>
{
  public static ThisType Read(Stream dest)
  {
    Span<byte> bytes = stackalloc byte[sizeof(WrappingType)];
    return BinaryPrimitives.ReadUInt64LittleEndian(bytes);
  }

  public static void Write(Stream dest, ThisType value)
  {
    Span<byte> bytes = stackalloc byte[sizeof(WrappingType)];
    BinaryPrimitives.WriteUInt64LittleEndian(bytes, value.Value);
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