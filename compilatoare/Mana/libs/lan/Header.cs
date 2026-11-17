namespace LAN;

public record struct MutableHeader
(
  Version Version,
  byte SymbolEncoding,
  uint SymbolCount,
  ulong SymbolSegmentSize,
  uint InstructionCount,
  ulong InstructionSegmentSize,
  uint GlobalCount,
  ulong GlobalSegmentSize
)
{
  public static implicit operator MutableHeader(Header h)
  {
    return new
    (
      h.Version,
      h.SymbolEncoding,
      h.SymbolCount,
      h.SymbolSegmentSize,
      h.InstructionCount,
      h.InstructionSegmentSize,
      h.GlobalCount,
      h.GlobalSegmentSize
    );
  }
}

public readonly record struct Header
(
  Version Version,
  byte SymbolEncoding,
  uint SymbolCount,
  ulong SymbolSegmentSize,
  uint InstructionCount,
  ulong InstructionSegmentSize,
  uint GlobalCount,
  ulong GlobalSegmentSize
)
{
  public static implicit operator Header(MutableHeader h)
  {
    return new
    (
      h.Version,
      h.SymbolEncoding,
      h.SymbolCount,
      h.SymbolSegmentSize,
      h.InstructionCount,
      h.InstructionSegmentSize,
      h.GlobalCount,
      h.GlobalSegmentSize
    );
  }
}