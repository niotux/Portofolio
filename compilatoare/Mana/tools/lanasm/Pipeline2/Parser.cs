using System.Buffers;
using LAN.Logging;
using LAN.Rules;

namespace LAN.Assembler.Pipeline2;

public class Parser
{
  public Logger Logger { get; }
  private List<Token> Tokens;
  private Parser(List<Token> tokens, Logger logger)
  {
    Tokens = tokens;
    Logger = logger;
  }

  public static (UnfinishedInstruction[] Instructions, Logger Logger) Run(List<Token> tokens)
    => Run((tokens, new Logger()));
  public static (UnfinishedInstruction[] Instructions, Logger Logger) Run((List<Token> Tokens, Logger Logger) input)
  {
    Parser parser = new(input.Tokens, input.Logger);
    return (parser.RunCore(), parser.Logger);
  }


  private UnfinishedInstruction[] RunCore()
  {
    if (Tokens.Count == 0)
    {
      return [];
    }

    var instructions = new UnfinishedInstruction[CountOpCodes()];
    for (int i = 0; i < instructions.Length; i++)
    {
      UnfinishedInstruction? instr = null;
      while (instr == null)
      {
        instr = MakeInstruction();
        if (Position == Tokens.Count - 1)
        {
          return instructions;
        }
      }
      instructions[i] = instr.Value;

    }
    return instructions;
  }

  private uint CountOpCodes()
  {
    uint count = 0;
    for (int i = 0; i < Tokens.Count; i++)
    {
      if (Tokens[i].Kind != TokenKind.Identifier)
      {
        continue;
      }
      if (Keywords.GetOpCode(Tokens[i].View) != OpCode.Unknown)
      {
        count++;
      }
    }
    return count;
  }

  private UnfinishedInstruction? MakeInstruction()
  {
    var token = CurrentToken;

    if (CurrentToken.Kind == TokenKind.Identifier)
    {
      byte op = Keywords.GetOpCode(CurrentToken.View);

      if (op == OpCode.Unknown)
      {
        Logger.Log(ParserMessages.UnknownOpCode, CurrentToken.View);
        SkipInstruction();
        return null;
      }

      return MakeInstructionAfterRuleShape(op);
    }
    if (CurrentToken.Kind == TokenKind.Symbol)
    {
      if (Grammar.InstructionEnd == CurrentToken.View)
      {
        if (!MoveToNextToken())
        {
          return null;
        }
        return null;
      }
    }

    Logger.Log(ParserMessages.ExpectedInstruction, CurrentToken.View);
    SkipToNextOp();
    return null;
  }

  private enum InstructionState
  {
    Template_0,
    Template_1,
    Value_0,
    Value_1,
    Value_2,
    Value_3,
  }

  private void SkipInstruction()
  {
    if (!MoveToNextToken())
    {
      return;
    }

    var state = InstructionState.Template_0;

    while (state <= InstructionState.Template_1)
    {
      if (CurrentToken.Kind == TokenKind.Symbol)
      {
        var view = CurrentToken.View;
        if (view == Grammar.InstructionEnd)
        {
          return;
        }
        if (view == Grammar.TemplateStart)
        {
          SkipTemplate();
          state++;
          continue;
        }
        Logger.Log(ParserMessages.UnexpectedSymbol, CurrentToken.View);
        if (!MoveToNextToken())
        {
          return;
        }
        state = InstructionState.Value_0;
        break;
      }
      state = InstructionState.Value_0;
    }

    while (state <= InstructionState.Value_3)
    {
      if (CurrentToken.Kind == TokenKind.Symbol)
      {
        if (CurrentToken.View == Grammar.InstructionEnd)
        {
          if (!MoveToNextToken())
          {
            return;
          }
          return;
        }
        Logger.Log(ParserMessages.UnexpectedSymbol, CurrentToken.View);
        if (!MoveToNextToken())
        {
          return;
        }
      }
      SkipValue();
      state++;
    }
    SkipToNextOp();
  }

  private void SkipToNextOp()
  {
    while (CurrentToken.Kind != TokenKind.Identifier && Keywords.GetOpCode(CurrentToken.View) == OpCode.Unknown)
    {
      if (!MoveToNextToken())
      {
        return;
      }
    }
  }

  private void SkipTemplate() => WalkSymbolChain();
  private void SkipValue() => WalkSymbolChain();

  private UnfinishedInstruction? MakeInstructionAfterRuleShape(byte op)
  {
    var rule = Syntax.GetRule(op);
    var opTok = CurrentToken;
    if (!MoveToNextToken())
    {
      return null;
    }

    var template0 = MakeTokenChain();
    if (template0 == null)
    {
      Logger.Log(ParserMessages.TooFewTemplates, default, opTok.View);
    }
    var template1 = MakeTokenChain();
    if (template1 == null)
    {
      Logger.Log(ParserMessages.TooFewTemplates, default, opTok.View);
    }
    // var value0 = MakeValue();
    // var value1 = MakeValue();
    // var value2 = MakeValue();
    // var value3 = MakeValue();

    return new(opTok, template0, template1, null, null, null, null);
  }


  private readonly ArrayPool<Token> TokenArrayPool = ArrayPool<Token>.Create();

  private enum SymbolChainType
  {
    Value,
    Template,
  }


  private Token[]? MakeTokenChain(SymbolChainType type)
  {
    if (CurrentToken.Kind != TokenKind.Symbol || CurrentToken.View != Grammar.TemplateStart)
    {
      return null;
    }
    if (!MoveToNextToken())
    {
      return null;
    }

    ushort count = WalkSymbolChain(type);
    var result = TokenArrayPool.Rent(count);

    // starting from '.' in case of template
    MoveToPrevToken((uint)count * 2);

    for (int i = 0; i < result.Length; i++)
    {
      MoveToNextToken();
      result[i] = CurrentToken;
      MoveToNextToken();
    }
    return result;
  }

  private ushort WalkSymbolChain(SymbolChainType type, Action? foreachToken = null)
  {
    ushort count = 0;
    do
    {
      if (CurrentToken.Kind == TokenKind.Symbol)
      {
        MoveToPrevToken();
        if (type == SymbolChainType.Template)
        {
          Logger.Log(ParserMessages.IncompleteTemplate, CurrentToken.View);
        }
        else
        {
          Logger.Log(ParserMessages.IncompleteValue, CurrentToken.View);
        }
        MoveToNextToken();
        return 0;
      }
      count++;
      if (!MoveToNextToken())
      {
        return count;
      }

      if (CurrentToken.Kind == TokenKind.Symbol)
      {
        if (type == SymbolChainType.Template)
        {
          if (CurrentToken.View == Grammar.TemplateStart)
          {
            return count;
          }
        }
        if (CurrentToken.View == Grammar.StructureCompositor)
        {
          continue;
        }
        if (CurrentToken.View == Grammar.InstructionEnd)
        {
          return count;
        }
        Logger.Log(ParserMessages.UnexpectedSymbol, CurrentToken.View);
        continue;
      }

      break;
    }
    while (MoveToNextToken());
    return count;
  }


  private int Position = 0;
  private bool MoveToNextToken(uint by = 1)
  {
    if (Position + by >= Tokens.Count)
    {
      return false;
    }
    Position += (int)by;
    return true;
  }
  private bool MoveToPrevToken(uint by = 1)
  {
    if (Position - by < 0)
    {
      return false;
    }
    Position -= (int)by;
    return true;
  }

  private Token CurrentToken => Tokens[Position];
}