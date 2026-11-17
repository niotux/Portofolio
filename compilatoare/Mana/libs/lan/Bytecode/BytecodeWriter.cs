using LAN.Bytecode.Types;


namespace LAN.Bytecode;

public class BytecodeWriter
{
  private readonly BytecodeTypeStream Stream;
  public BytecodeWriter(Stream dest, Version version, uint maxSymbolCount = uint.MaxValue)
  {
    if (!dest.CanRead || !dest.CanSeek || !dest.CanWrite)
    {
      throw new NotSupportedException("Stream must be readable, writeable and seekable.");
    }
    Stream = new(dest)
    {
      Position = BytecodePositions.EndOfHeader,
    };

    State = BytecodeWriterState.SymbolSegment;

    if (maxSymbolCount <= byte.MaxValue)
    {
      IndexSizeTag = BytecodeSymbolEncoding.U8;
    }
    else if (maxSymbolCount <= ushort.MaxValue)
    {
      IndexSizeTag = BytecodeSymbolEncoding.U16;
    }
    else
    {
      IndexSizeTag = BytecodeSymbolEncoding.U32;
    }
  }

  protected BytecodeWriterState State
  {
    get;
    set
    {
      if (value < field)
      {
        throw new InvalidOperationException("The bytecode stream's state can only advance, never regress.");
      }
      if (value == field)
      {
        return;
      }

      field = value;

      switch (value)
      {
        case BytecodeWriterState.InstructionSegment:
          Header.SymbolSegmentSize = Stream.Position
            - BytecodePositions.EndOfHeader;
          break;

        case BytecodeWriterState.GlobalSegment:
          Header.InstructionSegmentSize = Stream.Position
            - Header.SymbolSegmentSize
            - BytecodePositions.EndOfHeader;
          break;

        case BytecodeWriterState.Saved:
          Header.GlobalSegmentSize = Stream.Position
            - Header.InstructionSegmentSize
            - Header.SymbolSegmentSize
            - BytecodePositions.EndOfHeader;
          break;
      }
    }
  }
  protected enum BytecodeWriterState
  {
    SymbolSegment,
    InstructionSegment,
    GlobalSegment,
    Saved,
  }

  private MutableHeader Header;
  public void SaveHeader()
  {
    State = BytecodeWriterState.Saved;

    Stream.Position = BytecodePositions.MagicHeader;
    Stream.Write<U32>(BytecodeMagicValues.MagicHeader);
    Stream.Write<U16>(BytecodeMagicValues.MagicHeaderContinuation);

    Stream.Position = BytecodePositions.Version;
    Stream.Write<U8>(Header.Version.Major);
    Stream.Write<U8>(Header.Version.Minor);
    Stream.Write<U16>(Header.Version.Patch);

    Stream.Position = BytecodePositions.Zero;
    Stream.Write<U8>(0);

    Stream.Position = BytecodePositions.SymbolEncoding;
    Stream.Write<U8>(Header.SymbolEncoding);

    Stream.Position = BytecodePositions.SymbolCount;
    Stream.Write<U32>(Header.SymbolCount);
    Stream.Position = BytecodePositions.InstructionCount;
    Stream.Write<U32>(Header.InstructionCount);
    Stream.Position = BytecodePositions.GlobalCount;
    Stream.Write<U32>(Header.GlobalCount);

    Stream.Position = BytecodePositions.SymbolSegmentSize;
    Stream.Write<U64>(Header.SymbolSegmentSize);
    Stream.Position = BytecodePositions.InstructionSegmentSize;
    Stream.Write<U64>(Header.InstructionSegmentSize);
    Stream.Position = BytecodePositions.GlobalSegmentSize;
    Stream.Write<U64>(Header.GlobalSegmentSize);
    Stream.Position = 0;
    Stream.Length = BytecodePositions.EndOfHeader + Header.SymbolSegmentSize + Header.InstructionSegmentSize + Header.GlobalSegmentSize;
  }

  public void WriteOpCode(byte code)
  {
    State = BytecodeWriterState.InstructionSegment;

    Stream.Write<U8>(code);
    Header.InstructionCount++;
  }

  public void WriteTemplateTypeCount(ushort count)
  {
    State = BytecodeWriterState.InstructionSegment;

    Stream.Write<U16>(count);
  }

  private byte IndexSizeTag = BytecodeSymbolEncoding.U8;
  public void WriteSymbolIndex(uint index)
  {
    switch (IndexSizeTag)
    {
      case BytecodeSymbolEncoding.U8:
        Stream.Write<U8>((byte)index);
        break;
      case BytecodeSymbolEncoding.U16:
        Stream.Write<U16>((ushort)index);
        break;
      case BytecodeSymbolEncoding.U32:
        Stream.Write<U32>(index);
        break;
    }
  }

  public void WriteSymbol(Symbol constant)
  {
    State = BytecodeWriterState.SymbolSegment;
    Header.SymbolCount++;

    if (constant.Bytes == null)
    {
      ulong buff = constant.Buffer;
      if (buff <= byte.MaxValue)
      {
        Stream.Write<U8>(BytecodeSymbolTag.U8);
        Stream.Write<U8>((byte)buff);
      }
      else if (buff <= ushort.MaxValue)
      {
        Stream.Write<U8>(BytecodeSymbolTag.U16);
        Stream.Write<U16>((ushort)buff);
      }
      else if (buff <= uint.MaxValue)
      {
        Stream.Write<U8>(BytecodeSymbolTag.U32);
        Stream.Write<U32>((uint)buff);
      }
      else
      {
        Stream.Write<U8>(BytecodeSymbolTag.U64);
        Stream.Write<U64>(buff);
      }

      return;
    }

    ulong length = (ulong)constant.Bytes.LongLength;
    if (constant.Kind == SymbolKind.Identifier)
    {
      Stream.Write<U8>(BytecodeSymbolTag.CString);
      Stream.Write<CString>(constant.Bytes);
    }
    else if (length <= byte.MaxValue)
    {
      Stream.Write<U8>(BytecodeSymbolTag.U8Sized);
      Stream.Write<U8>((byte)length);
      Stream.Write<Sequence>(constant.Bytes);
    }
    else if (length <= ushort.MaxValue)
    {
      Stream.Write<U8>(BytecodeSymbolTag.U16Sized);
      Stream.Write<U16>((ushort)length);
      Stream.Write<Sequence>(constant.Bytes);
    }
    else if (length <= uint.MaxValue)
    {
      Stream.Write<U8>(BytecodeSymbolTag.U32Sized);
      Stream.Write<U32>((uint)length);
      Stream.Write<Sequence>(constant.Bytes);
    }
    else
    {
      Stream.Write<U8>(BytecodeSymbolTag.U64Sized);
      Stream.Write<U64>(length);
      Stream.Write<Sequence>(constant.Bytes);
    }
  }

  public void WriteGlobal(Global export)
  {
    State = BytecodeWriterState.GlobalSegment;
    WriteSymbolIndex(export.DeclarationInstruction);
    Header.GlobalCount++;
  }
}
