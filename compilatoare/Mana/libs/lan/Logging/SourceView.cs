using System;
using System.ComponentModel;
using System.Runtime.Serialization;
using LAN.Logging;

namespace LAN.Logging;

public readonly struct SourceView : IEquatable<SourceView>, IEquatable<string>, IEquatable<char>, IEquatable<ReadOnlySpan<char>>
{
  public required string Source { get; init; }
  public required uint Start { get; init; }
  public required uint Length { get; init; }
  public required uint Line { get; init; }
  public required uint Character { get; init; }

  public bool IsEmpty => Length == 0;
  public SourceView Trim(uint left, uint right = 0)
    => new()
    {
      Length = this.Length - left - right,
      Start = this.Start + left,
      Line = this.Line,
      Character = this.Character + left,
      Source = this.Source,
    };
  public SourceView First(uint count, uint skip = 0)
    => new()
    {
      Length = count,
      Start = this.Start + skip,
      Line = this.Line,
      Character = this.Character,
      Source = this.Source,
    };

  public SourceView Last(uint count, uint skip = 0)
    => new()
    {
      Length = count,
      Start = this.Start + this.Length - count - skip,
      Line = this.Line,
      Character = this.Character,
      Source = this.Source,
    };

  public ReadOnlySpan<char> Span => Source.AsSpan((int)Start, (int)Length);

  public override string ToString() => Span.ToString();

  public bool Equals(SourceView other) => Span.SequenceEqual(other.Span);
  public bool Equals(string? other) => other is not null && Span.SequenceEqual(other.AsSpan());
  public bool Equals(char other) => Span.Length == 1 && Span[0] == other;
  public bool Equals(ReadOnlySpan<char> other) => Span.SequenceEqual(other);

  [Browsable(false)]
  [EditorBrowsable(EditorBrowsableState.Never)]
  public override bool Equals(object? obj) => obj switch
  {
    SourceView t => Equals(t),
    string s => Equals(s),
    char c => Equals(c),
    _ => false,
  };

  public override int GetHashCode() => string.GetHashCode(Span);

  public static bool operator ==(SourceView left, SourceView right) => left.Equals(right);
  public static bool operator !=(SourceView left, SourceView right) => !left.Equals(right);

  public static bool operator ==(SourceView left, string? right) => left.Equals(right);
  public static bool operator !=(SourceView left, string? right) => !left.Equals(right);

  public static bool operator ==(string? left, SourceView right) => right.Equals(left);
  public static bool operator !=(string? left, SourceView right) => !right.Equals(left);

  public static bool operator ==(SourceView left, ReadOnlySpan<char> right) => left.Equals(right);
  public static bool operator !=(SourceView left, ReadOnlySpan<char> right) => !left.Equals(right);

  public static bool operator ==(ReadOnlySpan<char> left, SourceView right) => right.Equals(left);
  public static bool operator !=(ReadOnlySpan<char> left, SourceView right) => !right.Equals(left);

  public static bool operator ==(SourceView left, char right) => left.Equals(right);
  public static bool operator !=(SourceView left, char right) => !left.Equals(right);

  public static bool operator ==(char left, SourceView right) => right.Equals(left);
  public static bool operator !=(char left, SourceView right) => !right.Equals(left);

  public char this[uint index] => this.Span[(int)index];

  public static implicit operator SourceView(string text)
  {
    return new()
    {
      Length = (uint)text.Length,
      Line = 0,
      Character = 0,
      Source = text,
      Start = 0
    };
  }
}