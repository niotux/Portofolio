using System.Text;

namespace LAN;

public readonly record struct Instruction
(
  byte OpCode,
  Symbol[]? Template_0,
  Symbol[]? Template_1,
  Symbol[]? Value_0,
  Symbol[]? Value_1,
  Symbol[]? Value_2,
  Symbol[]? Value_3
);