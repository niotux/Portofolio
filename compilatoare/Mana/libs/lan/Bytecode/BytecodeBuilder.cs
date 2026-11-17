using LAN.Rules;

namespace LAN.Bytecode;


public class BytecodeBuilder
{
  public BytecodeBuilder()
  {
    RegisterSymbol(default);
  }

  private Version Version = Version.CurrentVersion;
  public BytecodeBuilder SetVersion(Version version)
  {
    Version = version;
    return this;
  }


  public BytecodeBuilder AppendInstructions(ReadOnlySpan<Instruction> instructions)
  {
    for (int i = 0; i < instructions.Length; i++)
    {
      AppendInstruction(instructions[i]);
    }
    return this;
  }

  public uint InstructionCount => (uint)Instructions.Count + (uint)(InstructionsReserve?.Count ?? 0);
  private List<Instruction> Instructions = new();
  private List<Instruction>? InstructionsReserve = null;
  public BytecodeBuilder AppendInstruction(Instruction instruction)
  {
    Instructions.Add(instruction);
    if (Syntax.GetRule(instruction.OpCode).Kind == InstructionKind.Global)
    {
      RegisterGlobal(new Global(InstructionCount));
    }
    RegisterSymbols(instruction.Template_0);
    RegisterSymbols(instruction.Template_1);
    RegisterSymbols(instruction.Value_0);
    RegisterSymbols(instruction.Value_1);
    RegisterSymbols(instruction.Value_2);
    RegisterSymbols(instruction.Value_3);

    if (Instructions.Count == int.MaxValue && InstructionsReserve == null)
    {
      InstructionsReserve = Instructions;
      Instructions = new();
    }
    return this;
  }

  public BytecodeBuilder RegisterSymbols(ReadOnlySpan<Symbol> symbols)
  {
    for (int i = 0; i < symbols.Length; i++)
    {
      RegisterSymbol(symbols[i]);
    }
    return this;
  }

  public uint SymbolCount => (uint)Symbols.Count + (uint)(SymbolsReserve?.Count ?? 0);
  private Dictionary<Symbol, uint> Symbols = new();
  private Dictionary<Symbol, uint>? SymbolsReserve = null;
  public BytecodeBuilder RegisterSymbol(Symbol constant)
  {
    if (SymbolsReserve?.ContainsKey(constant) ?? false)
    {
      return this;
    }

    if (!Symbols.TryAdd(constant, 0))
    {
      return this;
    }

    if (Symbols.Count == int.MaxValue && SymbolsReserve == null)
    {
      SymbolsReserve = Symbols;
      Symbols = new();
    }
    return this;
  }

  public uint GlobalCount => (uint)Globals.Count + (uint)(GlobalsReserve?.Count ?? 0);
  private HashSet<Global> Globals = new();
  private HashSet<Global>? GlobalsReserve = null;
  public BytecodeBuilder RegisterGlobal(Global global)
  {
    if (GlobalsReserve?.Contains(global) ?? false)
    {
      return this;
    }

    if (!Globals.Add(global))
    {
      return this;
    }

    if (Globals.Count == int.MaxValue && GlobalsReserve == null)
    {
      GlobalsReserve = Globals;
      Globals = new();
    }
    return this;
  }

  public Instruction GetInstruction(uint index)
  {
    if (InstructionCount > int.MaxValue)
    {
      if (index > int.MaxValue)
      {
        return Instructions[(int)(index - int.MaxValue)];
      }
      return InstructionsReserve?[(int)index] ?? default;
    }
    return Instructions[(int)index];
  }

  public void WriteBytes(Stream stream)
  {
    BytecodeWriter streamer = new(stream, Version, SymbolCount);

    WriteIteration<Dictionary<Symbol, uint>, KeyValuePair<Symbol, uint>>
    (
      streamer,
      Symbols,
      SymbolsReserve,
      (writer, kvp, idx) =>
      {
        writer.WriteSymbol(kvp.Key);
        if (SymbolsReserve?.ContainsKey(kvp.Key) ?? false)
        {
          SymbolsReserve[kvp.Key] = idx;
        }
      }
    );
    WriteIteration<List<Instruction>, Instruction>
    (
      streamer,
      Instructions,
      InstructionsReserve,
      (writer, instr, idx) => WriteInstruction(writer, instr)
    );
    WriteIteration<HashSet<Global>, Global>
    (
      streamer,
      Globals,
      GlobalsReserve,
      (writer, global, idx) => writer.WriteGlobal(global)
    );

    streamer.SaveHeader();
  }

  private void WriteInstruction(BytecodeWriter writer, Instruction instr)
  {
    writer.WriteOpCode(instr.OpCode);
    var rule = Syntax.GetRule(instr.OpCode);

    writer.WriteTemplateTypeCount((ushort)(instr.Template_0?.Length ?? 0));
    WriteSymbolSeq(writer, rule.Template_0, instr.Template_0);

    writer.WriteTemplateTypeCount((ushort)(instr.Template_1?.Length ?? 0));
    WriteSymbolSeq(writer, rule.Template_1, instr.Template_1);

    WriteSymbolSeq(writer, rule.Value_0, instr.Value_0);
    WriteSymbolSeq(writer, rule.Value_1, instr.Value_1);
    WriteSymbolSeq(writer, rule.Value_2, instr.Value_2);
    WriteSymbolSeq(writer, rule.Value_3, instr.Value_3);
  }

  private void WriteSymbolSeq(BytecodeWriter writer, ISymbolRule rule, Symbol[]? symbols)
  {
    if (rule.Appearence == Appearence.MustNotExist)
    {
      return;
    }

    if (symbols == null)
    {
      throw new InvalidDataException("A required symbol sequence is missing.");
    }

    for (ushort i = 0; i < symbols.Length; i++)
    {
      WriteSymbolIndex(writer, rule, symbols[i]);
    }
  }

  private void WriteSymbolIndex(BytecodeWriter writer, ISymbolRule rule, Symbol symbol)
  {
    if (rule.Appearence == Appearence.MustNotExist)
    {
      return;
    }
    uint index = Symbols.TryGetValue(symbol, out index) ? index : 0;
    writer.WriteSymbolIndex(index);
  }

  private delegate void ElementWriter<TElement>(BytecodeWriter writer, TElement e, uint index);
  private void WriteIteration<TEnumerable, TElement>(BytecodeWriter writer, TEnumerable enumerable, TEnumerable? reserve, ElementWriter<TElement> elementWriter)
    where TEnumerable : IEnumerable<TElement>
  {
    uint i = 1;
    foreach (var c in enumerable)
    {
      elementWriter(writer, c, i);
      i++;
    }
    if (reserve != null)
    {
      foreach (var c in reserve)
      {
        elementWriter(writer, c, i);
        i++;
      }
    }
  }
}