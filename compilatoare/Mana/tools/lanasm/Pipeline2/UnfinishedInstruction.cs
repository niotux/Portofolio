namespace LAN.Assembler.Pipeline2;

public record struct UnfinishedInstruction
(
  Token Op,
  Token[]? Template_0,
  Token[]? Template_1,
  Token[]? Value_0,
  Token[]? Value_1,
  Token[]? Value_2,
  Token[]? Value_3
);