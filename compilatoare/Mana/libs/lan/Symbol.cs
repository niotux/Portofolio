using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using LAN;

namespace LAN;

public enum SymbolKind
{
  Identifier,
  Literal,
}

public readonly struct Symbol : IEquatable<Symbol>
{
  public required SymbolKind Kind { get; init; }
  public required ulong Buffer { get; init; }
  public required byte[]? Bytes { get; init; }
  public uint Line { get; init; }
  public uint Char { get; init; }

  public override int GetHashCode()
  {
    HashCode a = new();
    if (Bytes == null)
    {
      a.Add(Buffer);
    }
    else
    {
      a.AddBytes(Bytes);
    }
    a.Add(Kind);
    return a.ToHashCode();
  }

  public override bool Equals(object? obj)
  {
    if (obj is not Symbol c)
    {
      return false;
    }
    return c.Equals(this);
  }

  public bool Equals(Symbol obj)
  {
    if (obj.Bytes == this.Bytes)
    {
      return true;
    }
    if (obj.Bytes == null)
    {
      return obj.Buffer == this.Buffer;
    }
    if (obj.Bytes.SequenceEqual(this.Bytes))
    {
      return true;
    }
    return false;
  }

  public override string ToString()
  {
    string storage = Bytes is null
      ? $"inline 0x{Buffer:X16}"
      : $"heap[{Bytes.Length}] {Convert.ToHexString(Bytes)}";
    return $"{Kind}({storage}) @ {Line}:{Char}";
  }

  public static bool operator ==(Symbol l, Symbol r) => l.Equals(r);
  public static bool operator !=(Symbol l, Symbol r) => !l.Equals(r);
}

public static class SymbolArray
{
  public static string ToString(this Symbol[] inst)
  {
    StringBuilder sb = new();
    sb.Append("{ ");
    for (uint i = 0; i < inst.Length; i++)
    {
      sb.Append(inst[i].ToString());
      sb.Append(", ");
    }
    sb.Append(" }");
    return sb.ToString();
  }
}