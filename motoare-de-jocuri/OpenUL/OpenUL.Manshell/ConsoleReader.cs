using System.Text;

namespace OpenUL.Manshell;

internal static class ConsoleReader
{
  private enum ScopeKind
  {
    None,
    Type,
    Array,
    String,
  }

  public static string ReadColoredLine()
  {

    var input = new StringBuilder();
    int cursor = 0;

    // Tracks the terminal's true cursor position for logical index `screenCursor`.
    // Re-synced from Console.CursorLeft/Top after every raw write instead of being
    // predicted, so real wrapping/scrolling (which the terminal already handles
    // correctly on its own) is simply observed rather than modeled.
    int screenLeft = Console.CursorLeft;
    int screenTop = Console.CursorTop;
    int screenCursor = 0;

    var history = CommandHistory.All;
    int historyIndex = history.Count;
    string draft = "";

    void MoveTo(int targetIndex)
    {
      int delta = targetIndex - screenCursor;
      if (delta == 0)
      {
        return;
      }

      int width = int.Max(Console.BufferWidth, 1);
      int total = screenLeft + delta;
      int newLeft = ((total % width) + width) % width;
      int newTop = screenTop + (total - newLeft) / width;
      newTop = int.Clamp(newTop, 0, Console.BufferHeight - 1);

      try
      {
        Console.SetCursorPosition(newLeft, newTop);
      }
      catch (ArgumentOutOfRangeException)
      {
        return;
      }

      screenLeft = newLeft;
      screenTop = newTop;
      screenCursor = targetIndex;
    }

    void WriteFrom(int fromIndex, int clearTrailing)
    {
      MoveTo(fromIndex);
      for (int i = fromIndex; i < input.Length; i++)
      {
        Console.ForegroundColor = GetColor(input.ToString(0, i), input[i]);
        Console.Write(input[i]);
      }
      for (int i = 0; i < clearTrailing; i++)
      {
        Console.Write(' ');
      }

      screenLeft = Console.CursorLeft;
      screenTop = Console.CursorTop;
      screenCursor = input.Length + clearTrailing;

      MoveTo(cursor);
    }

    void ReplaceInput(string text)
    {
      int oldLength = input.Length;
      input.Clear();
      input.Append(text);
      cursor = input.Length;
      WriteFrom(0, int.Max(0, oldLength - input.Length));
    }

    while (true)
    {
      var key = Console.ReadKey(intercept: true);

      if (key.Key == ConsoleKey.Enter)
      {
        break;
      }

      if (key.Key == ConsoleKey.LeftArrow)
      {
        if (cursor > 0)
        {
          cursor--;
          MoveTo(cursor);
        }
        continue;
      }

      if (key.Key == ConsoleKey.RightArrow)
      {
        if (cursor < input.Length)
        {
          cursor++;
          MoveTo(cursor);
        }
        continue;
      }

      if (key.Key == ConsoleKey.Home)
      {
        cursor = 0;
        MoveTo(cursor);
        continue;
      }

      if (key.Key == ConsoleKey.End)
      {
        cursor = input.Length;
        MoveTo(cursor);
        continue;
      }

      if (key.Key == ConsoleKey.UpArrow)
      {
        if (history.Count == 0)
        {
          continue;
        }
        if (historyIndex == history.Count)
        {
          draft = input.ToString();
        }
        if (historyIndex > 0)
        {
          historyIndex--;
          ReplaceInput(history[historyIndex]);
        }
        continue;
      }

      if (key.Key == ConsoleKey.DownArrow)
      {
        if (historyIndex >= history.Count)
        {
          continue;
        }
        historyIndex++;
        ReplaceInput(historyIndex == history.Count ? draft : history[historyIndex]);
        continue;
      }

      if (key.Key == ConsoleKey.LeftArrow && key.Modifiers.HasFlag(ConsoleModifiers.Control))
      {
        int start = FindWordStart(input, cursor);
        if (start < cursor)
        {
          cursor = start;
          MoveTo(cursor);
        }
        continue;
      }

      if (key.Key == ConsoleKey.RightArrow && key.Modifiers.HasFlag(ConsoleModifiers.Control))
      {
        int end = FindWordEnd(input, cursor);
        if (end > cursor)
        {
          cursor = end;
          MoveTo(cursor);
        }
        continue;
      }

      if (key.Key == ConsoleKey.Backspace && key.Modifiers.HasFlag(ConsoleModifiers.Control))
      {
        int start = FindWordStart(input, cursor);
        if (start < cursor)
        {
          int removed = cursor - start;
          input.Remove(start, removed);
          cursor = start;
          WriteFrom(cursor, removed);
        }
        continue;
      }

      if (key.Key == ConsoleKey.Delete && key.Modifiers.HasFlag(ConsoleModifiers.Control))
      {
        int end = FindWordEnd(input, cursor);
        if (end > cursor)
        {
          int removed = end - cursor;
          input.Remove(cursor, removed);
          WriteFrom(cursor, removed);
        }
        continue;
      }

      if (key.Key == ConsoleKey.Delete)
      {
        if (cursor < input.Length)
        {
          input.Remove(cursor, 1);
          WriteFrom(cursor, 1);
        }
        continue;
      }

      if (key.Key == ConsoleKey.Backspace)
      {
        if (cursor == 0)
        {
          continue;
        }
        input.Remove(cursor - 1, 1);
        cursor--;
        WriteFrom(cursor, 1);
        continue;
      }

      if (char.IsControl(key.KeyChar))
      {
        continue;
      }

      input.Insert(cursor, key.KeyChar);
      cursor++;
      WriteFrom(cursor - 1, 0);
    }

    Console.ResetColor();
    Console.WriteLine();

    var line = input.ToString();
    CommandHistory.Add(line);
    return line;
  }

  // Word-boundary helpers for Ctrl+Backspace / Ctrl+Delete: skip whitespace adjacent to
  // the cursor, then consume one contiguous run of the same character class (word chars
  // vs. symbol chars), so deletion stops at the next symbol/whitespace boundary.
  private static int FindWordStart(StringBuilder input, int cursor)
  {
    int start = cursor;
    while (start > 0 && char.IsWhiteSpace(input[start - 1]))
    {
      start--;
    }
    if (start > 0)
    {
      bool word = IsWordChar(input[start - 1]);
      while (start > 0 && !char.IsWhiteSpace(input[start - 1]) && IsWordChar(input[start - 1]) == word)
      {
        start--;
      }
    }
    return start;
  }

  private static int FindWordEnd(StringBuilder input, int cursor)
  {
    int end = cursor;
    int n = input.Length;
    while (end < n && char.IsWhiteSpace(input[end]))
    {
      end++;
    }
    if (end < n)
    {
      bool word = IsWordChar(input[end]);
      while (end < n && !char.IsWhiteSpace(input[end]) && IsWordChar(input[end]) == word)
      {
        end++;
      }
    }
    return end;
  }

  private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

  private static ConsoleColor GetColor(ReadOnlySpan<char> typedSoFar, char c)
  {
    var (scopes, argIndex, inToken, tokenStart) = ScanState(typedSoFar);
    ScopeKind currentScope = scopes.Count > 0 ? scopes[^1] : ScopeKind.None;

    // The quote character itself (opening or closing) is always red. Every other
    // character typed while a string is still open keeps the string's dark-red
    // color no matter what it looks like - digit, bracket, whatever - so a string
    // stays a string whether it's top-level or nested inside an array/type.
    if (c is Syntax.StringDelimiter)
    {
      return ConsoleColor.Red;
    }

    if (currentScope == ScopeKind.String)
    {
      return ConsoleColor.DarkRed;
    }

    if (c is Syntax.TypeStart or Syntax.TypeEnd or Syntax.ArrayStart or Syntax.ArrayEnd)
    {
      return ConsoleColor.DarkGray;
    }

    if (c is Syntax.End)
    {
      return ConsoleColor.Yellow;
    }

    if (currentScope == ScopeKind.Type)
    {
      return ConsoleColor.DarkCyan;
    }

    // Only color a digit green when every character typed so far in this same
    // token is also a digit. That keeps a bare number like "123" green while
    // stopping a digit that trails a word, like the "1" in "var1", from being
    // colored as though it started a fresh number.
    if (char.IsNumber(c) && IsNumericToken(typedSoFar, tokenStart))
    {
      return ConsoleColor.Green;
    }

    int effectiveArgIndex = inToken ? argIndex : argIndex + 1;
    return effectiveArgIndex == 0 ? ConsoleColor.DarkBlue : ConsoleColor.White;
  }

  private static bool IsNumericToken(ReadOnlySpan<char> typedSoFar, int tokenStart)
  {
    if (tokenStart < 0)
    {
      return true;
    }

    for (int i = tokenStart; i < typedSoFar.Length; i++)
    {
      if (!char.IsDigit(typedSoFar[i]))
      {
        return false;
      }
    }

    return true;
  }

  // Walks the already-typed text using the same token/separator rules as Parser
  // so the live coloring of the next keystroke matches how the line will parse.
  // Scopes are tracked as a stack (rather than a single kind + depth) so nesting
  // of different bracket kinds resolves correctly - in particular, a string
  // opened inside an array (or a type) is recognized as a string scope rather
  // than being swallowed by the outer array/type scope.
  private static (List<ScopeKind> Scopes, int ArgIndex, bool InToken, int TokenStart) ScanState(ReadOnlySpan<char> text)
  {
    var scopes = new List<ScopeKind>();
    int argIndex = -1;
    bool inToken = false;
    int tokenStart = -1;

    for (int i = 0; i < text.Length; i++)
    {
      char c = text[i];
      ScopeKind current = scopes.Count > 0 ? scopes[^1] : ScopeKind.None;

      if (current == ScopeKind.None)
      {
        if (c == Syntax.End)
        {
          argIndex = -1;
          inToken = false;
          tokenStart = -1;
          continue;
        }
        if (c == ' ')
        {
          inToken = false;
          tokenStart = -1;
          continue;
        }
        if (!inToken)
        {
          argIndex++;
          inToken = true;
          tokenStart = i;
        }

        switch (c)
        {
          case Syntax.TypeStart:
            scopes.Add(ScopeKind.Type);
            break;

          case Syntax.ArrayStart:
            scopes.Add(ScopeKind.Array);
            break;

          case Syntax.StringDelimiter:
            scopes.Add(ScopeKind.String);
            break;
        }
        continue;
      }

      if (current == ScopeKind.String)
      {
        // Inside a string, only the matching delimiter has any syntactic meaning;
        // everything else (including bracket characters) is just string content.
        if (c == Syntax.StringDelimiter)
        {
          scopes.RemoveAt(scopes.Count - 1);
        }
        continue;
      }

      switch (c)
      {
        case Syntax.TypeStart:
          scopes.Add(ScopeKind.Type);
          break;

        case Syntax.ArrayStart:
          scopes.Add(ScopeKind.Array);
          break;

        case Syntax.StringDelimiter:
          scopes.Add(ScopeKind.String);
          break;

        case Syntax.TypeEnd when current == ScopeKind.Type:
          scopes.RemoveAt(scopes.Count - 1);
          break;

        case Syntax.ArrayEnd when current == ScopeKind.Array:
          scopes.RemoveAt(scopes.Count - 1);
          break;
      }
    }

    return (scopes, argIndex, inToken, tokenStart);
  }
}