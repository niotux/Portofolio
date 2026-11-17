using System.Data;
using LAN.Bytecode.Types;


namespace LAN.Bytecode;

public class BytecodeReader
{
  private readonly BytecodeTypeStream Stream;
  public BytecodeReader(Stream dest)
  {
    Stream = new(dest)
    {
      Position = 0
    };

    State = BytecodeReaderState.LoadedHeader;
  }

  protected BytecodeReaderState State
  {
    get;
    set
    {
      if (value < field)
      {
        throw new InvalidOperationException("The bytecode stream's state can only advance, never regress.");
      }
      field = value;
    }
  }
  protected enum BytecodeReaderState
  {
    LoadedHeader,
    SymbolSegment,
    InstructionSegment,
    GlobalSegment,
  }

  private byte IndexSizeTag;
  public Header LoadHeader()
  {
    State = BytecodeReaderState.LoadedHeader;

    Stream.Position = BytecodePositions.MagicHeader;
    uint magic = Stream.Read<U32>();
    ushort magicContinuation = Stream.Read<U16>();

    Stream.Position = BytecodePositions.Version;
    var version = new Version(Stream.Read<U8>(), Stream.Read<U8>(), Stream.Read<U16>());

    Stream.Position = BytecodePositions.Zero;
    Stream.Read<U8>();

    Stream.Position = BytecodePositions.SymbolEncoding;
    IndexSizeTag = Stream.Read<U8>();

    Stream.Position = BytecodePositions.SymbolCount;
    uint symbolCount = Stream.Read<U32>();
    uint instructionCount = Stream.Read<U32>();
    uint globalCount = Stream.Read<U32>();

    Stream.Position = BytecodePositions.SymbolSegmentSize;
    ulong symbolSegmentSize = Stream.Read<U64>();
    ulong instructionSegmentSize = Stream.Read<U64>();
    ulong globalSegmentSize = Stream.Read<U64>();

    Stream.Position = BytecodePositions.EndOfHeader;

    return new Header(version, IndexSizeTag, symbolCount, symbolSegmentSize, instructionCount, instructionSegmentSize, globalCount, globalSegmentSize);
  }

  public byte ReadOpCode()
  {
    State = BytecodeReaderState.InstructionSegment;

    return Stream.Read<U8>();
  }

  public ushort ReadTemplateTypeCount()
  {
    State = BytecodeReaderState.InstructionSegment;

    return Stream.Read<U16>();
  }

  public uint ReadSymbolIndex()
  {
    switch (IndexSizeTag)
    {
      case BytecodeSymbolEncoding.U8:
        return Stream.Read<U8>();
      case BytecodeSymbolEncoding.U16:
        return Stream.Read<U16>();
      case BytecodeSymbolEncoding.U32:
        return Stream.Read<U32>();
    }
    return default;
  }

  public Symbol ReadSymbol()
  {
    State = BytecodeReaderState.SymbolSegment;

    byte tag = Stream.Read<U8>();
    ulong size = 0;
    switch (tag)
    {
      case BytecodeSymbolTag.CString:
        return new()
        {
          Kind = SymbolKind.Identifier,
          Bytes = Stream.Read<CString>(),
          Buffer = 0,
        };

      case BytecodeSymbolTag.U8:
        return new()
        {
          Kind = SymbolKind.Literal,
          Bytes = null,
          Buffer = Stream.Read<U8>(),
        };

      case BytecodeSymbolTag.U16:
        return new()
        {
          Kind = SymbolKind.Literal,
          Bytes = null,
          Buffer = Stream.Read<U16>(),
        };

      case BytecodeSymbolTag.U32:
        return new()
        {
          Kind = SymbolKind.Literal,
          Bytes = null,
          Buffer = Stream.Read<U32>(),
        };

      case BytecodeSymbolTag.U64:
        return new()
        {
          Kind = SymbolKind.Literal,
          Bytes = null,
          Buffer = Stream.Read<U64>(),
        };

      case BytecodeSymbolTag.U8Sized:
        size = Stream.Read<U8>();
        return new()
        {
          Kind = SymbolKind.Literal,
          Bytes = Stream.Read<Sequence, ulong>(size),
          Buffer = 0,
        };

      case BytecodeSymbolTag.U16Sized:
        size = Stream.Read<U16>();
        return new()
        {
          Kind = SymbolKind.Literal,
          Bytes = Stream.Read<Sequence, ulong>(size),
          Buffer = 0,
        };

      case BytecodeSymbolTag.U32Sized:
        size = Stream.Read<U32>();
        return new()
        {
          Kind = SymbolKind.Literal,
          Bytes = Stream.Read<Sequence, ulong>(size),
          Buffer = 0,
        };

      case BytecodeSymbolTag.U64Sized:
        size = Stream.Read<U64>();
        return new()
        {
          Kind = SymbolKind.Literal,
          Bytes = Stream.Read<Sequence, ulong>(size),
          Buffer = 0,
        };
    }
    throw new InvalidDataException("Bytecode format is either corrupted or uses a newer version");
  }

  public Global ReadGlobal()
  {
    State = BytecodeReaderState.GlobalSegment;
    return new()
    {
      DeclarationInstruction = ReadSymbolIndex(),
    };
  }
}
