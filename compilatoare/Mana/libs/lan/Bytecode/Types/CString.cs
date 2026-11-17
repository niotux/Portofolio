namespace LAN.Bytecode.Types;

using ThisType = CString;
using WrappingType = byte[];
public readonly record struct CString
(
  WrappingType Value
)
: IBytecodeReadableType<ThisType>, IBytecodeWriteableType<ThisType>
{
  public static ThisType Read(Stream dest)
  {
    long pos = dest.Position;

    byte current;
    int count = 0;
    do
    {
      current = (byte)dest.ReadByte();
      count++;
    }
    while (current != 0);

    var memory = new byte[count];
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