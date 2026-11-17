namespace LAN.Bytecode;

internal static class BytecodeSizes
{
  public const ulong MagicHeader = 6;
  public const ulong Version = 1 + 1 + 2;
  public const ulong Zero = 1;
  public const ulong SymbolEncoding = 1;
  public const ulong SymbolCount = 4;

  public const ulong InstructionCount = 4;
  public const ulong GlobalCount = 4;

  public const ulong SymbolSegmentSize = 8;
  public const ulong InstructionSegmentSize = 8;
  public const ulong GlobalSegmentSize = 8;
}

internal static class BytecodePositions
{
  public const ulong MagicHeader = 0;
  public const ulong Version = MagicHeader + BytecodeSizes.MagicHeader;
  public const ulong Zero = Version + BytecodeSizes.Version;
  public const ulong SymbolEncoding = Zero + BytecodeSizes.Zero;
  public const ulong SymbolCount = SymbolEncoding + BytecodeSizes.SymbolEncoding;

  public const ulong InstructionCount = SymbolCount + BytecodeSizes.SymbolCount;
  public const ulong GlobalCount = InstructionCount + BytecodeSizes.InstructionCount;

  public const ulong SymbolSegmentSize = GlobalCount + BytecodeSizes.GlobalCount;
  public const ulong InstructionSegmentSize = SymbolSegmentSize + BytecodeSizes.SymbolSegmentSize;
  public const ulong GlobalSegmentSize = InstructionSegmentSize + BytecodeSizes.InstructionSegmentSize;
  public const ulong EndOfHeader = GlobalSegmentSize + BytecodeSizes.GlobalSegmentSize;
}

internal static class BytecodeMagicValues
{
  public const uint MagicHeader = 0x5F4E414C;
  public const ushort MagicHeaderContinuation = 0x4C49;
  public const uint HeaderSizeInU64Units = (uint)BytecodePositions.EndOfHeader / 8;
}
