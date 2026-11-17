using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using LAN.Logging;

namespace LAN.Assembler.Pipeline2;

public readonly struct Token
{
  public TokenKind Kind { get; init; }
  public SourceView View { get; init; }
}

public enum TokenKind
{
  Identifier,
  Number,
  String,
  Symbol,
}

public class Tokenizer
{
  public Logger Logger { get; }
  private Tokenizer(string source, Logger logger)
  {
    Source = source;
    Logger = logger;
  }

  public static (List<Token> Tokens, Logger Logger) Run(string source)
    => Run((source, new Logger()));
  public static (List<Token> Tokens, Logger Logger) Run((string Source, Logger Logger) input)
  {
    Tokenizer tok = new(input.Source, input.Logger);
    return (tok.RunCore(), tok.Logger);
  }

  private readonly string Source;

  private uint Position;
  private uint Line = 1;
  private uint Char = 1;

  private uint Length => (uint)Source.Length;
  private char Current => Source[(int)Position];

  private List<Token> RunCore()
  {
    List<Token> tokens = new();
    while (TokenizeNext(out var token))
    {
      if (token != null)
      {
        tokens.Add(token.Value);
      }
    }
    return tokens;
  }

  private bool TokenizeNext(out Token? output)
  {
    output = null;
    if (!SkipTrivia())
    {
      return false;
    }

    char c = Current;
    uint start = Position;
    uint line = Line;
    uint column = Char;

    if (Grammar.IsIdentifierStart(c))
    {
      output = Tokenize(start, line, column, ScanFrom(Position, Grammar.IsIdentifierContinue) - start, TokenKind.Identifier);
      return true;
    }

    if (Grammar.IsNumberStart(c))
    {
      output = Tokenize(start, line, column, ScanFrom(Position + 1, Grammar.IsNumberContinue) - start, TokenKind.Number);
      return true;
    }

    if (IsNegativeNumber())
    {
      output = Tokenize(start, line, column, ScanFrom(Position + 2, Grammar.IsNumberContinue) - start, TokenKind.Number);
      return true;
    }

    if (c == Grammar.StringQuote)
    {
      output = TokenizeString(start, line, column);
      return true;
    }

    foreach (string symbol in Grammar.Symbols)
    {
      if (Matches(symbol))
      {
        output = Tokenize(start, line, column, (uint)symbol.Length, TokenKind.Symbol);
        return true;
      }
    }

    Illegal(line, column);
    output = null;
    return true;
  }

  private bool SkipTrivia()
  {
    while (Position < Length)
    {
      if (Matches(Grammar.Comment))
      {
        SkipToLineEnd();
        continue;
      }
      if (Grammar.IsBoundary(Current))
      {
        Advance(1);
        continue;
      }
      return true;
    }
    return false;
  }

  private void SkipToLineEnd()
  {
    while (Position < Length && Current != '\n')
    {
      Advance(1);
    }
  }

  private bool IsNegativeNumber()
    => Current == Grammar.NegativeSign
    && Position + 1 < Length
    && char.IsAsciiDigit(Source[(int)Position + 1]);

  private Token? TokenizeString(uint start, uint line, uint column)
  {
    uint i = Position + 1;
    while (i < Length && Source[(int)i] != Grammar.StringQuote && Source[(int)i] != '\n')
    {
      i++;
    }

    bool terminated = i < Length && Source[(int)i] == Grammar.StringQuote;
    if (terminated)
    {
      return Tokenize(start, line, column, i + 1 - start, TokenKind.String);
    }

    Logger.Log
    (
      TokenizerMessages.UnterminatedString,
      new()
      {
        Source = Source,
        Line = line,
        Character = column,
        Start = start,
        Length = 1
      }
    );
    Advance(i - start);
    return null;
  }

  private void Illegal(uint line, uint column)
  {
    Logger.Log
    (
      TokenizerMessages.InvalidCharacter,
      new()
      {
        Source = Source,
        Line = line,
        Character = column,
        Start = Position,
        Length = 1
      }
    );
    Advance(1);
  }

  private uint ScanFrom(uint from, Func<char, bool> predicate)
  {
    uint end = from;
    while (end < Length && predicate(Source[(int)end]))
    {
      end++;
    }
    return end;
  }

  private bool Matches(string terminal)
  {
    if (Position + (uint)terminal.Length > Length)
    {
      return false;
    }
    return Source.AsSpan((int)Position, terminal.Length).SequenceEqual(terminal);
  }

  private Token Tokenize(uint start, uint line, uint column, uint length, TokenKind kind)
  {
    Advance(length);
    return new()
    {
      Kind = kind,
      View = new()
      {
        Source = Source,
        Start = start,
        Length = length,
        Line = line,
        Character = column,
      }
    };
  }

  // The one place the cursor moves; keeps line/char honest for every consumer.
  private void Advance(uint count)
  {
    for (uint i = 0; i < count; i++)
    {
      if (Current == '\n')
      {
        Line++;
        Char = 1;
      }
      else
      {
        Char++;
      }
      Position++;
    }
  }
}
