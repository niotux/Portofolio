using System;

namespace Texp.Core;

/// <summary>The text in the command line and its cursor. Fixed capacity, no allocation while typing.</summary>
public sealed class LineEditor
{
  public const int Capacity = 1024;
  readonly char[] _buffer = new char[Capacity];
  int _length;
  int _cursor;

  public ReadOnlySpan<char> Text
  {
    get { return _buffer.AsSpan(0, _length); }
  }

  public int Cursor
  {
    get { return _cursor; }
  }

  public bool IsEmpty
  {
    get { return _length == 0; }
  }

  public void Clear()
  {
    _length = 0;
    _cursor = 0;
  }

  public void Set(ReadOnlySpan<char> text)
  {
    int n = Math.Min(text.Length, Capacity);
    text.Slice(0, n).CopyTo(_buffer);
    _length = n;
    _cursor = n;
  }

  public void Insert(char c)
  {
    if (_length >= Capacity)
    {
      return;
    }
    Array.Copy(_buffer, _cursor, _buffer, _cursor + 1, _length - _cursor);
    _buffer[_cursor++] = c;
    _length++;
  }

  public void Insert(ReadOnlySpan<char> text)
  {
    for (int i = 0; i < text.Length; i++)
    {
      Insert(text[i]);
    }
  }

  public void Backspace()
  {
    if (_cursor == 0)
    {
      return;
    }
    Array.Copy(_buffer, _cursor, _buffer, _cursor - 1, _length - _cursor);
    _cursor--;
    _length--;
  }

  public void DeleteForward()
  {
    if (_cursor >= _length)
    {
      return;
    }
    Array.Copy(_buffer, _cursor + 1, _buffer, _cursor, _length - _cursor - 1);
    _length--;
  }

  /// <summary>Deletes the word before the cursor (Ctrl+W in a shell; here Alt+Backspace).</summary>
  public void BackspaceWord()
  {
    int start = _cursor;
    while (start > 0 && _buffer[start - 1] == ' ')
    {
      start--;
    }
    while (start > 0 && _buffer[start - 1] != ' ')
    {
      start--;
    }
    Array.Copy(_buffer, _cursor, _buffer, start, _length - _cursor);
    _length -= _cursor - start;
    _cursor = start;
  }

  public void Move(int delta)
  {
    _cursor = Math.Clamp(_cursor + delta, 0, _length);
  }

  public void MoveTo(int position)
  {
    _cursor = Math.Clamp(position, 0, _length);
  }
}
