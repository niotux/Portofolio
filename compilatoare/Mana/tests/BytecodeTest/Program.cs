using LAN;
using LAN.Bytecode;

Instruction[] instructions =
[
  new
  (
    OpCode.Fun,
    [
      new() { Buffer = 4, Bytes = null, Kind = SymbolKind.Literal}
    ],
    [],
    [
      new() { Buffer = default, Bytes = "Main"u8.ToArray(), Kind = SymbolKind.Identifier }
    ],
    [],
    [],
    []
  ),
  new
  (
    OpCode.Set,
    [
      new() { Buffer = 4, Bytes = null, Kind = SymbolKind.Literal}
    ],
    [],
    [
      new() { Buffer = default, Bytes = "a"u8.ToArray(), Kind = SymbolKind.Identifier }
    ],
    [
      new() { Buffer = 10, Bytes = null, Kind = SymbolKind.Literal}
    ],
    [],
    []
  ),
  new
  (
    OpCode.Set,
    [
      new() { Buffer = 4, Bytes = null, Kind = SymbolKind.Literal}
    ],
    [],
    [
      new() { Buffer = default, Bytes = "b"u8.ToArray(), Kind = SymbolKind.Identifier }
    ],
    [
      new() { Buffer = 20, Bytes = null, Kind = SymbolKind.Literal}
    ],
    [],
    []
  ),
  new
  (
    OpCode.Add,
    [
      new() { Buffer = 4, Bytes = null, Kind = SymbolKind.Literal}
    ],
    [],
    [
      new() { Buffer = default, Bytes = "result"u8.ToArray(), Kind = SymbolKind.Identifier }
    ],
    [
      new() { Buffer = default, Bytes = "a"u8.ToArray(), Kind = SymbolKind.Identifier }
    ],
    [
      new() { Buffer = default, Bytes = "b"u8.ToArray(), Kind = SymbolKind.Identifier }
    ],
    []
  ),
  new
  (
    OpCode.Ret,
    [],
    [],
    [
      new() { Buffer = default, Bytes = "result"u8.ToArray(), Kind = SymbolKind.Identifier }
    ],
    [],
    [],
    []
  ),
];

BytecodeBuilder bb = new();
bb.AppendInstructions(instructions);

using var output = File.Open("outputshort.lan", FileMode.Create);
bb.WriteBytes(output);