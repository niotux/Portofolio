using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace UnivKit.Unmanaged.RefCounted;

/// <summary>
/// A reference-counted, unmanaged-heap-backed string backed by <see cref="RCArray{char}"/>.
/// Mirrors the public API of <see cref="string"/>.
/// </summary>
/// <remarks>
/// <b>Bug notice:</b> <see cref="RCArray{T}.AsSpan(int)"/> and <see cref="RCArray{T}.AsSpan(int,int)"/>
/// add byte offsets instead of element offsets, making them incorrect for multi-byte types such as
/// <c>char</c>. All sliced span access in <see cref="RCString"/> is therefore performed via
/// <c>chars.AsSpan().Slice(...)</c> to work around this until the underlying bug is fixed.
/// </remarks>
public struct RCString
: IRefCounted<RCString>,
  IComparable<RCString>,
  IComparable,
  ICloneable,
  IEnumerable<char>
{
  private RCArray<char> chars;

  // =========================================================================
  // Static fields
  // =========================================================================

  public static readonly RCString Empty = new(ReadOnlySpan<char>.Empty);

  // =========================================================================
  // Constructors
  // =========================================================================

  public RCString(ReadOnlySpan<char> value)
  {
    chars = new RCArray<char>(value.Length);
    value.CopyTo(chars.AsSpan());
  }

  public RCString(char[] value) : this(value.AsSpan()) { }
  public RCString(char[] value, int startIndex, int length) : this(value.AsSpan(startIndex, length)) { }

  public RCString(char c, int count)
  {
    chars = new RCArray<char>(count);
    chars.AsSpan().Fill(c);
  }

  /// <summary>
  /// Adopts an existing <see cref="RCArray{char}"/> without copying.
  /// The caller transfers ownership and must not use the array afterward.
  /// </summary>
  private RCString(RCArray<char> chars) => this.chars = chars;

  // =========================================================================
  // Core properties
  // =========================================================================

  public int Length => chars.Length;
  public long RefCount => chars.RefCount;
  public bool IsEmpty => chars.Length == 0;

  public char this[int index] => chars[index];
  public char this[Index index] => chars[index];

  // =========================================================================
  // Span access
  // =========================================================================

  // Always slice from the root span to avoid the byte-offset bug in RCArray<T>.AsSpan(int[,int]).
  public ReadOnlySpan<char> AsSpan() => chars.AsSpan();
  public ReadOnlySpan<char> AsSpan(int start) => chars.AsSpan().Slice(start);
  public ReadOnlySpan<char> AsSpan(int start, int length) => chars.AsSpan().Slice(start, length);
  public ReadOnlySpan<char> AsSpan(Range range) => chars.AsSpan()[range];

  // =========================================================================
  // Ref-counting & lifetime
  // =========================================================================

  public static RCString operator ++(RCString s)
  {
    s.chars++;
    return s;
  }
  public static RCString operator --(RCString s)
  {
    s.chars--;
    return s;
  }
  public void Dispose() => this--;

  /// <summary>Increments the ref-count and returns the same logical string.</summary>
  public object Clone() => ++this;

  // =========================================================================
  // Conversions
  // =========================================================================

  public static implicit operator RCString(string value) => new(value.AsSpan());
  public static implicit operator ReadOnlySpan<char>(RCString value) => value.AsSpan();

  /// <summary>Allocates a managed <see cref="string"/> from the current contents.</summary>
  public override string ToString() => new string(AsSpan());

  public char[] ToCharArray() => AsSpan().ToArray();
  public char[] ToCharArray(int startIndex, int length) => AsSpan(startIndex, length).ToArray();

  public void CopyTo(Span<char> destination) => AsSpan().CopyTo(destination);
  public bool TryCopyTo(Span<char> destination) => AsSpan().TryCopyTo(destination);
  public void CopyTo(int sourceIndex, char[] destination, int destinationIndex, int count)
      => AsSpan(sourceIndex, count).CopyTo(destination.AsSpan(destinationIndex));

  // =========================================================================
  // Equality
  // =========================================================================

  public bool Equals(RCString other) => AsSpan().SequenceEqual(other.AsSpan());
  public bool Equals(RCString other, StringComparison comparison) => AsSpan().Equals(other.AsSpan(), comparison);
  public bool Equals(ReadOnlySpan<char> other) => AsSpan().SequenceEqual(other);
  public bool Equals(ReadOnlySpan<char> other, StringComparison comparison) => AsSpan().Equals(other, comparison);

  public override bool Equals(object? obj) => obj switch
  {
    RCString s => Equals(s),
    string s => Equals(s.AsSpan()),
    _ => false,
  };

  public static bool Equals(RCString a, RCString b) => a.Equals(b);
  public static bool Equals(RCString a, RCString b, StringComparison comparison) => a.Equals(b, comparison);

  public static bool operator ==(RCString a, RCString b) => a.Equals(b);
  public static bool operator !=(RCString a, RCString b) => !a.Equals(b);

  // =========================================================================
  // Hashing
  // =========================================================================

  public override int GetHashCode() => string.GetHashCode(AsSpan());
  public int GetHashCode(StringComparison comparison) => string.GetHashCode(AsSpan(), comparison);

  // =========================================================================
  // Comparison
  // =========================================================================

  public int CompareTo(RCString other) => AsSpan().CompareTo(other.AsSpan(), StringComparison.CurrentCulture);

  public int CompareTo(object? obj) => obj switch
  {
    RCString s => CompareTo(s),
    null => 1,
    _ => throw new ArgumentException($"Object must be of type {nameof(RCString)}."),
  };

  public static int Compare(RCString a, RCString b)
      => a.AsSpan().CompareTo(b.AsSpan(), StringComparison.CurrentCulture);
  public static int Compare(RCString a, RCString b, StringComparison comparison)
      => a.AsSpan().CompareTo(b.AsSpan(), comparison);
  public static int Compare(RCString a, RCString b, bool ignoreCase)
      => Compare(a, b, ignoreCase ? StringComparison.CurrentCultureIgnoreCase : StringComparison.CurrentCulture);
  public static int Compare(RCString a, RCString b, bool ignoreCase, CultureInfo? culture)
  {
    return (culture ?? CultureInfo.CurrentCulture).CompareInfo.Compare
    (
      a.ToString(),
      b.ToString(),
      ignoreCase
        ? CompareOptions.IgnoreCase
        : CompareOptions.None
    );
  }
  public static int Compare(RCString a, RCString b, CultureInfo? culture, CompareOptions options)
      => (culture ?? CultureInfo.CurrentCulture).CompareInfo.Compare(a.ToString(), b.ToString(), options);
  public static int Compare(RCString a, int ia, RCString b, int ib, int length)
      => string.Compare(a.ToString(), ia, b.ToString(), ib, length);
  public static int Compare(RCString a, int ia, RCString b, int ib, int length, StringComparison comparison)
      => string.Compare(a.ToString(), ia, b.ToString(), ib, length, comparison);
  public static int Compare(RCString a, int ia, RCString b, int ib, int length, bool ignoreCase)
      => string.Compare(a.ToString(), ia, b.ToString(), ib, length, ignoreCase);
  public static int Compare(RCString a, int ia, RCString b, int ib, int length, bool ignoreCase, CultureInfo? culture)
      => string.Compare(a.ToString(), ia, b.ToString(), ib, length, ignoreCase, culture);
  public static int Compare(RCString a, int ia, RCString b, int ib, int length, CultureInfo? culture, CompareOptions options)
      => string.Compare(a.ToString(), ia, b.ToString(), ib, length, culture, options);
  public static int CompareOrdinal(RCString a, RCString b)
      => a.AsSpan().CompareTo(b.AsSpan(), StringComparison.Ordinal);
  public static int CompareOrdinal(RCString a, int ia, RCString b, int ib, int length)
      => string.CompareOrdinal(a.ToString(), ia, b.ToString(), ib, length);

  // =========================================================================
  // Null / empty checks
  // =========================================================================

  public static bool IsNullOrEmpty(RCString? value) => value is null || value.Value.Length == 0;

  public static bool IsNullOrWhiteSpace(RCString? value)
  {
    if (value is null) return true;
    foreach (char c in value.Value.AsSpan())
    {
      if (!char.IsWhiteSpace(c))
      {
        return false;
      }
    }
    return true;
  }

  // =========================================================================
  // Contains
  // =========================================================================

  public bool Contains(char value) => AsSpan().Contains(value);
  public bool Contains(char value, StringComparison comparison) => IndexOf(value, comparison) >= 0;
  public bool Contains(ReadOnlySpan<char> value, StringComparison comparison = StringComparison.CurrentCulture)
      => AsSpan().Contains(value, comparison);

  // =========================================================================
  // StartsWith / EndsWith
  // =========================================================================

  public bool StartsWith(char value) => Length > 0 && chars[0] == value;
  public bool StartsWith(ReadOnlySpan<char> value, StringComparison comparison = StringComparison.CurrentCulture)
      => AsSpan().StartsWith(value, comparison);
  public bool StartsWith(string value, bool ignoreCase, CultureInfo? culture)
      => (culture ?? CultureInfo.CurrentCulture).CompareInfo.IsPrefix(
             ToString(), value, ignoreCase ? CompareOptions.IgnoreCase : CompareOptions.None);

  public bool EndsWith(char value) => Length > 0 && chars[Length - 1] == value;
  public bool EndsWith(ReadOnlySpan<char> value, StringComparison comparison = StringComparison.CurrentCulture)
      => AsSpan().EndsWith(value, comparison);
  public bool EndsWith(string value, bool ignoreCase, CultureInfo? culture)
      => (culture ?? CultureInfo.CurrentCulture).CompareInfo.IsSuffix(
             ToString(), value, ignoreCase ? CompareOptions.IgnoreCase : CompareOptions.None);

  // =========================================================================
  // IndexOf
  // =========================================================================

  public int IndexOf(char value) => AsSpan().IndexOf(value);

  public int IndexOf(char value, int startIndex)
  {
    int r = AsSpan(startIndex).IndexOf(value);
    return r < 0 ? -1 : r + startIndex;
  }

  public int IndexOf(char value, int startIndex, int count)
  {
    int r = AsSpan(startIndex, count).IndexOf(value);
    return r < 0 ? -1 : r + startIndex;
  }

  public int IndexOf(char value, StringComparison comparison)
  {
    Span<char> needle = stackalloc char[] { value };
    return AsSpan().IndexOf(needle, comparison);
  }

  public int IndexOf(ReadOnlySpan<char> value, StringComparison comparison = StringComparison.CurrentCulture)
      => AsSpan().IndexOf(value, comparison);

  public int IndexOf(ReadOnlySpan<char> value, int startIndex, StringComparison comparison = StringComparison.CurrentCulture)
  {
    int r = AsSpan(startIndex).IndexOf(value, comparison);
    return r < 0 ? -1 : r + startIndex;
  }

  public int IndexOf(ReadOnlySpan<char> value, int startIndex, int count, StringComparison comparison = StringComparison.CurrentCulture)
  {
    int r = AsSpan(startIndex, count).IndexOf(value, comparison);
    return r < 0 ? -1 : r + startIndex;
  }

  // =========================================================================
  // IndexOfAny
  // =========================================================================

  public int IndexOfAny(char[] anyOf) => AsSpan().IndexOfAny(anyOf);

  public int IndexOfAny(char[] anyOf, int startIndex)
  {
    int r = AsSpan(startIndex).IndexOfAny(anyOf);
    return r < 0 ? -1 : r + startIndex;
  }

  public int IndexOfAny(char[] anyOf, int startIndex, int count)
  {
    int r = AsSpan(startIndex, count).IndexOfAny(anyOf);
    return r < 0 ? -1 : r + startIndex;
  }

  // =========================================================================
  // LastIndexOf
  // =========================================================================

  public int LastIndexOf(char value) => AsSpan().LastIndexOf(value);

  public int LastIndexOf(char value, int startIndex)
      => AsSpan(0, startIndex + 1).LastIndexOf(value);

  public int LastIndexOf(char value, int startIndex, int count)
  {
    // 'startIndex' is the index of the last char to examine;
    // the search window is [startIndex - count + 1 .. startIndex].
    int start = startIndex + 1 - count;
    int r = AsSpan(start, count).LastIndexOf(value);
    return r < 0 ? -1 : r + start;
  }

  // Linguistic overloads delegate to string to preserve CompareInfo semantics.
  public int LastIndexOf(ReadOnlySpan<char> value, StringComparison comparison = StringComparison.CurrentCulture)
      => AsSpan().LastIndexOf(value, comparison);
  public int LastIndexOf(ReadOnlySpan<char> value, int startIndex, StringComparison comparison = StringComparison.CurrentCulture)
      => AsSpan(startIndex).LastIndexOf(value, comparison);
  public int LastIndexOf(ReadOnlySpan<char> value, int startIndex, int count, StringComparison comparison = StringComparison.CurrentCulture)
      => AsSpan(startIndex, count).LastIndexOf(value, comparison);

  // =========================================================================
  // LastIndexOfAny
  // =========================================================================

  public int LastIndexOfAny(char[] anyOf) => AsSpan().LastIndexOfAny(anyOf);

  public int LastIndexOfAny(char[] anyOf, int startIndex)
      => AsSpan(0, startIndex + 1).LastIndexOfAny(anyOf);

  public int LastIndexOfAny(char[] anyOf, int startIndex, int count)
  {
    int start = startIndex + 1 - count;
    int r = AsSpan(start, count).LastIndexOfAny(anyOf);
    return r < 0 ? -1 : r + start;
  }

  // =========================================================================
  // Substring / slicing
  // =========================================================================

  public RCString Substring(int startIndex) => new(AsSpan(startIndex));
  public RCString Substring(int startIndex, int length) => new(AsSpan(startIndex, length));

  /// <summary>C# range-syntax slice: <c>str[1..^1]</c></summary>
  public RCString this[Range range] => new(AsSpan(range));

  // =========================================================================
  // Insert
  // =========================================================================

  public RCString Insert(int startIndex, ReadOnlySpan<char> value)
  {
    var arr = new RCArray<char>(Length + value.Length);
    var span = arr.AsSpan();
    AsSpan(0, startIndex).CopyTo(span);
    value.CopyTo(span.Slice(startIndex));
    AsSpan(startIndex).CopyTo(span.Slice(startIndex + value.Length));
    return new(arr);
  }

  // =========================================================================
  // Remove
  // =========================================================================

  public RCString Remove(int startIndex) => Substring(0, startIndex);

  public RCString Remove(int startIndex, int count)
  {
    int newLen = Length - count;
    var arr = new RCArray<char>(newLen);
    var span = arr.AsSpan();
    AsSpan(0, startIndex).CopyTo(span);
    AsSpan(startIndex + count).CopyTo(span.Slice(startIndex));
    return new(arr);
  }

  // =========================================================================
  // Replace
  // =========================================================================

  /// <summary>Single-character replacement — implemented directly without a managed string allocation.</summary>
  public RCString Replace(char oldChar, char newChar)
  {
    var arr = new RCArray<char>(Length);
    var span = arr.AsSpan();
    AsSpan().CopyTo(span);
    for (int i = 0; i < span.Length; i++)
      if (span[i] == oldChar) span[i] = newChar;
    return new(arr);
  }

  // Multi-character / linguistic overloads delegate to string for correctness.
  public RCString Replace(ReadOnlySpan<char> oldValue, ReadOnlySpan<char> newValue)
    => new(ToString().Replace(oldValue.ToString(), newValue.ToString()));
  public RCString Replace(ReadOnlySpan<char> oldValue, ReadOnlySpan<char> newValue, StringComparison comparison)
    => new(ToString().Replace(oldValue.ToString(), newValue.ToString(), comparison));
  public RCString Replace(ReadOnlySpan<char> oldValue, ReadOnlySpan<char> newValue, bool ignoreCase, CultureInfo? culture)
    => new(ToString().Replace(oldValue.ToString(), newValue.ToString(), ignoreCase, culture));

  // =========================================================================
  // ReplaceLineEndings
  // =========================================================================

  public RCString ReplaceLineEndings()
    => new(ToString().ReplaceLineEndings());

  public RCString ReplaceLineEndings(ReadOnlySpan<char> replacementText)
    => new(ToString().ReplaceLineEndings(replacementText.ToString()));

  // =========================================================================
  // Trim
  // =========================================================================

  public RCString Trim() => new(AsSpan().Trim());
  public RCString Trim(char trimChar) => new(AsSpan().Trim(trimChar));
  public RCString Trim(params char[]? trimChars)
    => trimChars is null or { Length: 0 } ? Trim() : new(AsSpan().Trim(trimChars));

  public RCString TrimStart() => new(AsSpan().TrimStart());
  public RCString TrimStart(char trimChar) => new(AsSpan().TrimStart(trimChar));
  public RCString TrimStart(params char[]? trimChars)
    => trimChars is null or { Length: 0 } ? TrimStart() : new(AsSpan().TrimStart(trimChars));

  public RCString TrimEnd() => new(AsSpan().TrimEnd());
  public RCString TrimEnd(char trimChar) => new(AsSpan().TrimEnd(trimChar));
  public RCString TrimEnd(params char[]? trimChars)
      => trimChars is null or { Length: 0 } ? TrimEnd() : new(AsSpan().TrimEnd(trimChars));

  // =========================================================================
  // Padding
  // =========================================================================

  public RCString PadLeft(int totalWidth) => new(ToString().PadLeft(totalWidth));
  public RCString PadLeft(int totalWidth, char paddingChar) => new(ToString().PadLeft(totalWidth, paddingChar));
  public RCString PadRight(int totalWidth) => new(ToString().PadRight(totalWidth));
  public RCString PadRight(int totalWidth, char paddingChar) => new(ToString().PadRight(totalWidth, paddingChar));

  // =========================================================================
  // Case conversion
  // =========================================================================

  public RCString ToUpper() => new(ToString().ToUpper());
  public RCString ToUpper(CultureInfo? culture) => new(ToString().ToUpper(culture));
  public RCString ToUpperInvariant() => new(ToString().ToUpperInvariant());
  public RCString ToLower() => new(ToString().ToLower());
  public RCString ToLower(CultureInfo? culture) => new(ToString().ToLower(culture));
  public RCString ToLowerInvariant() => new(ToString().ToLowerInvariant());

  // =========================================================================
  // Unicode normalization
  // =========================================================================

  public bool IsNormalized() => ToString().IsNormalized();
  public bool IsNormalized(NormalizationForm form) => ToString().IsNormalized(form);
  public RCString Normalize() => new(ToString().Normalize());
  public RCString Normalize(NormalizationForm form) => new(ToString().Normalize(form));

  // =========================================================================
  // Split
  // =========================================================================

  public RCArray<RCString> Split(char separator, StringSplitOptions options = StringSplitOptions.None)
    => SplitInternal(new ReadOnlySpan<char>(ref separator), int.MaxValue, options);

  public RCArray<RCString> Split(char separator, int count, StringSplitOptions options = StringSplitOptions.None)
    => SplitInternal(new ReadOnlySpan<char>(ref separator), count, options);

  public RCArray<RCString> Split(ReadOnlySpan<char> separators)
    => SplitInternal(separators, int.MaxValue, StringSplitOptions.None);

  public RCArray<RCString> Split(ReadOnlySpan<char> separators, int count, StringSplitOptions options = StringSplitOptions.None)
    => SplitInternal(separators, count, options);

  public RCArray<RCString> Split(string? separator, StringSplitOptions options = StringSplitOptions.None)
    => SplitInternal(separator.AsSpan(), int.MaxValue, options);

  public RCArray<RCString> Split(ReadOnlySpan<string> separators, StringSplitOptions options = StringSplitOptions.None)
  {
    // String array requires searching for whole sequences
    // Logic for string sequences differs from 'Any' char logic
    return SplitInternalStrings(separators, int.MaxValue, options);
  }

  private RCArray<RCString> SplitInternal(ReadOnlySpan<char> separators, int count, StringSplitOptions options)
  {
    if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
    ReadOnlySpan<char> span = AsSpan();

    // Short-circuit for empty string or count 0/1
    if (count <= 1 || span.IsEmpty)
    {
      return CreateSoleValueArray(options);
    }

    // Build lists of where the separators are
    // We use a safe buffer size (1024) to avoid stack overflow
    var indices = new ValueListBuilder<int>(stackalloc int[1024]);
    var lengths = new ValueListBuilder<int>(stackalloc int[1024]);

    if (separators.IsEmpty)
    {
      // Default behavior: Split on any whitespace
      MakeSeparatorListWhitespace(span, ref indices, ref lengths, count);
    }
    else
    {
      // Split on any character in the separators span
      MakeSeparatorListAny(span, separators, ref indices, ref lengths, count);
    }

    RCArray<RCString> result;
    if (options == StringSplitOptions.None)
      result = SplitWithoutPostProcessing(span, indices.AsSpan(), lengths.AsSpan(), count);
    else
      result = SplitWithPostProcessing(span, indices.AsSpan(), lengths.AsSpan(), count, options);

    indices.Dispose();
    lengths.Dispose();
    return result;
  }

  private void MakeSeparatorListAny(ReadOnlySpan<char> span, ReadOnlySpan<char> separators, ref ValueListBuilder<int> indices, ref ValueListBuilder<int> lengths, int count)
  {
    int lastPos = 0;
    while (indices.Length < count - 1)
    {
      int idx = span.Slice(lastPos).IndexOfAny(separators);
      if (idx == -1) break;

      indices.Append(lastPos + idx);
      lengths.Append(1);
      lastPos += idx + 1;
    }
  }

  private void MakeSeparatorListWhitespace(ReadOnlySpan<char> span, ref ValueListBuilder<int> indices, ref ValueListBuilder<int> lengths, int count)
  {
    int lastPos = 0;
    while (indices.Length < count - 1 && lastPos < span.Length)
    {
      // Find the next whitespace character
      int idx = -1;
      for (int i = lastPos; i < span.Length; i++)
      {
        if (char.IsWhiteSpace(span[i]))
        {
          idx = i;
          break;
        }
      }

      if (idx == -1) break;

      indices.Append(idx);
      lengths.Append(1);
      lastPos = idx + 1;
    }
  }

  private RCArray<RCString> SplitWithPostProcessing(ReadOnlySpan<char> span, ReadOnlySpan<int> indices, ReadOnlySpan<int> lengths, int count, StringSplitOptions options)
  {
    bool removeEmpty = options.HasFlag(StringSplitOptions.RemoveEmptyEntries);
    bool trimEntries = options.HasFlag(StringSplitOptions.TrimEntries);

    // Pass 1: Count valid segments
    int finalCount = 0;
    int lastPos = 0;
    for (int i = 0; i < indices.Length; i++)
    {
      var seg = span.Slice(lastPos, indices[i] - lastPos);
      if (trimEntries) seg = seg.Trim();
      if (!removeEmpty || !seg.IsEmpty) finalCount++;
      lastPos = indices[i] + lengths[i];
    }
    var remainder = span.Slice(lastPos);
    if (trimEntries) remainder = remainder.Trim();
    if (!removeEmpty || !remainder.IsEmpty) finalCount++;

    // Pass 2: Fill
    var result = new RCArray<RCString>(finalCount);
    int resIdx = 0;
    lastPos = 0;
    for (int i = 0; i < indices.Length && resIdx < finalCount; i++)
    {
      var seg = span.Slice(lastPos, indices[i] - lastPos);
      if (trimEntries) seg = seg.Trim();
      if (!removeEmpty || !seg.IsEmpty) result[resIdx++] = new RCString(seg);
      lastPos = indices[i] + lengths[i];
    }
    if (resIdx < finalCount) result[resIdx] = new RCString(remainder);

    return result;
  }

  private RCArray<RCString> SplitWithoutPostProcessing(ReadOnlySpan<char> span, ReadOnlySpan<int> indices, ReadOnlySpan<int> lengths, int count)
  {
    int resultCount = indices.Length + 1;
    var result = new RCArray<RCString>(resultCount);
    int lastPos = 0;

    for (int i = 0; i < indices.Length; i++)
    {
      result[i] = new RCString(span.Slice(lastPos, indices[i] - lastPos));
      lastPos = indices[i] + lengths[i];
    }

    result[resultCount - 1] = new RCString(span.Slice(lastPos));
    return result;
  }

  private RCArray<RCString> CreateSoleValueArray(StringSplitOptions options)
  {
    ReadOnlySpan<char> span = AsSpan();
    bool removeEmpty = options.HasFlag(StringSplitOptions.RemoveEmptyEntries);
    bool trimEntries = options.HasFlag(StringSplitOptions.TrimEntries);

    if (trimEntries) span = span.Trim();

    if (removeEmpty && span.IsEmpty)
    {
      return new RCArray<RCString>(0);
    }

    var result = new RCArray<RCString>(1);
    result[0] = new RCString(span);
    return result;
  }

  private RCArray<RCString> SplitInternalStrings(ReadOnlySpan<string> separators, int count, StringSplitOptions options)
  {
    if (separators == null || separators.Length == 0)
      return SplitInternal(default, count, options);

    ReadOnlySpan<char> span = AsSpan();
    var indices = new ValueListBuilder<int>(stackalloc int[1024]);
    var lengths = new ValueListBuilder<int>(stackalloc int[1024]);

    int lastPos = 0;
    while (indices.Length < count - 1)
    {
      int earliestIdx = -1;
      int bestLen = 0;

      for (int i = 0; i < separators.Length; i++)
      {
        string sep = separators[i];
        if (string.IsNullOrEmpty(sep)) continue;

        int idx = span.Slice(lastPos).IndexOf(sep);
        if (idx != -1 && (earliestIdx == -1 || idx < earliestIdx))
        {
          earliestIdx = idx;
          bestLen = sep.Length;
        }
      }

      if (earliestIdx == -1) break;

      indices.Append(lastPos + earliestIdx);
      lengths.Append(bestLen);
      lastPos += earliestIdx + bestLen;
    }

    RCArray<RCString> result = (options == StringSplitOptions.None)
        ? SplitWithoutPostProcessing(span, indices.AsSpan(), lengths.AsSpan(), count)
        : SplitWithPostProcessing(span, indices.AsSpan(), lengths.AsSpan(), count, options);

    indices.Dispose();
    lengths.Dispose();
    return result;
  }

  /// <summary>
  /// Helper to convert a managed string array into an unmanaged RCArray of RCStrings.
  /// </summary>
  public static RCArray<RCString> ToRCArray(string[] parts)
  {
    var result = new RCArray<RCString>(parts.Length);
    for (int i = 0; i < parts.Length; i++)
    {
      // Create a new RCString from the managed string's span
      result[i] = new RCString(parts[i].AsSpan());
    }
    return result;
  }

  // =========================================================================
  // Concat (static)
  // =========================================================================

  /// <summary>Two-span concat — zero managed-string allocation.</summary>
  public static RCString Concat(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
  {
    var arr = new RCArray<char>(a.Length + b.Length);
    var span = arr.AsSpan();
    a.CopyTo(span);
    b.CopyTo(span.Slice(a.Length));
    return new(arr);
  }

  public static RCString Concat(ReadOnlySpan<char> a, ReadOnlySpan<char> b, ReadOnlySpan<char> c)
  {
    var arr = new RCArray<char>(a.Length + b.Length + c.Length);
    var span = arr.AsSpan();
    a.CopyTo(span);
    b.CopyTo(span.Slice(a.Length));
    c.CopyTo(span.Slice(a.Length + b.Length));
    return new(arr);
  }

  public static RCString Concat(ReadOnlySpan<char> a, ReadOnlySpan<char> b, ReadOnlySpan<char> c, ReadOnlySpan<char> d)
  {
    var arr = new RCArray<char>(a.Length + b.Length + c.Length + d.Length);
    var span = arr.AsSpan();
    a.CopyTo(span);
    b.CopyTo(span.Slice(a.Length));
    c.CopyTo(span.Slice(a.Length + b.Length));
    d.CopyTo(span.Slice(a.Length + b.Length + c.Length));
    return new(arr);
  }

  /// <summary>Concatenates a sequence of <see cref="RCString"/> values without allocating managed strings.</summary>
  public static RCString Concat(IEnumerable<RCString> values)
  {
    // Buffer first so we can do a single-allocation two-pass (measure → fill).
    var parts = new List<RCString>();
    foreach (var v in values) parts.Add(v);
    if (parts.Count == 0) return Empty;

    int total = 0;
    for (int i = 0; i < parts.Count; i++) total += parts[i].Length;

    var arr = new RCArray<char>(total);
    var span = arr.AsSpan();
    int pos = 0;
    for (int i = 0; i < parts.Count; i++)
    {
      parts[i].AsSpan().CopyTo(span.Slice(pos));
      pos += parts[i].Length;
    }
    return new(arr);
  }

  // Delegate to string.Concat for object / generic overloads.
  public static RCString Concat(object? a) => new(string.Concat(a));
  public static RCString Concat(object? a, object? b) => new(string.Concat(a, b));
  public static RCString Concat(object? a, object? b, object? c) => new(string.Concat(a, b, c));
  public static RCString Concat(params object?[] args) => new(string.Concat(args));
  public static RCString Concat<T>(IEnumerable<T> values) => new(string.Concat(values));
  public static RCString Concat(IEnumerable<string?> values) => new(string.Concat(values));

  // =========================================================================
  // Concat operators
  // =========================================================================

  public static RCString operator +(RCString a, RCString b) => Concat(a.AsSpan(), b.AsSpan());
  public static RCString operator +(RCString a, string b) => Concat(a.AsSpan(), b.AsSpan());
  public static RCString operator +(string a, RCString b) => Concat(a.AsSpan(), b.AsSpan());
  public static RCString operator +(RCString a, char b)
  {
    Span<char> bSpan = stackalloc char[] { b };
    return Concat(a.AsSpan(), bSpan);
  }

  // =========================================================================
  // Join (static)
  // =========================================================================

  public static RCString Join(char separator, IEnumerable<RCString> values)
  {
    Span<char> sep = stackalloc char[] { separator };
    return JoinRCCore(sep, values);
  }

  public static RCString Join(ReadOnlySpan<char> separator, IEnumerable<RCString> values)
      => JoinRCCore(separator, values);

  public static RCString Join(char separator, params RCString[] values)
  {
    Span<char> sep = stackalloc char[] { separator };
    return JoinRCCore(sep, values);
  }

  public static RCString Join(ReadOnlySpan<char> separator, params RCString[] values)
      => JoinRCCore(separator, values);

  private static RCString JoinRCCore(ReadOnlySpan<char> separator, IEnumerable<RCString> values)
  {
    var parts = new List<RCString>();
    foreach (var v in values) parts.Add(v);
    if (parts.Count == 0) return Empty;

    int total = separator.Length * (parts.Count - 1);
    for (int i = 0; i < parts.Count; i++) total += parts[i].Length;

    var arr = new RCArray<char>(total);
    var span = arr.AsSpan();
    int pos = 0;
    for (int i = 0; i < parts.Count; i++)
    {
      if (i > 0) { separator.CopyTo(span.Slice(pos)); pos += separator.Length; }
      parts[i].AsSpan().CopyTo(span.Slice(pos));
      pos += parts[i].Length;
    }
    return new(arr);
  }

  // Delegate to string.Join for non-RCString overloads.
  public static RCString Join<T>(char separator, IEnumerable<T> values) => new(string.Join(separator, values));
  public static RCString Join<T>(string? separator, IEnumerable<T> values) => new(string.Join(separator, values));
  public static RCString Join(char separator, params object?[] values) => new(string.Join(separator, values));
  public static RCString Join(string? separator, params object?[] values) => new(string.Join(separator, values));
  public static RCString Join(string? separator, IEnumerable<string?> values) => new(string.Join(separator, values));
  public static RCString Join(char separator, string?[] value, int startIndex, int count) => new(string.Join(separator, value, startIndex, count));
  public static RCString Join(string? separator, string?[] value, int startIndex, int count) => new(string.Join(separator, value, startIndex, count));

  // =========================================================================
  // Format (static) — delegates to string.Format
  // =========================================================================

  public static RCString Format(string format, object? a) => new(string.Format(format, a));
  public static RCString Format(string format, object? a, object? b) => new(string.Format(format, a, b));
  public static RCString Format(string format, object? a, object? b, object? c) => new(string.Format(format, a, b, c));
  public static RCString Format(string format, params object?[] args) => new(string.Format(format, args));
  public static RCString Format(IFormatProvider? provider, string format, object? a) => new(string.Format(provider, format, a));
  public static RCString Format(IFormatProvider? provider, string format, object? a, object? b) => new(string.Format(provider, format, a, b));
  public static RCString Format(IFormatProvider? provider, string format, object? a, object? b, object? c) => new(string.Format(provider, format, a, b, c));
  public static RCString Format(IFormatProvider? provider, string format, params object?[] args) => new(string.Format(provider, format, args));
  public static RCString Format<TArg0>(IFormatProvider? provider, CompositeFormat format, TArg0 arg0)
      => new(string.Format(provider, format, arg0));
  public static RCString Format<TArg0, TArg1>(IFormatProvider? provider, CompositeFormat format, TArg0 arg0, TArg1 arg1)
      => new(string.Format(provider, format, arg0, arg1));
  public static RCString Format<TArg0, TArg1, TArg2>(IFormatProvider? provider, CompositeFormat format, TArg0 arg0, TArg1 arg1, TArg2 arg2)
      => new(string.Format(provider, format, arg0, arg1, arg2));
  public static RCString Format(IFormatProvider? provider, CompositeFormat format, params object?[] args)
      => new(string.Format(provider, format, args));
  public static RCString Format(IFormatProvider? provider, CompositeFormat format, ReadOnlySpan<object?> args)
      => new(string.Format(provider, format, args.ToArray()));

  // =========================================================================
  // Enumeration
  // =========================================================================

  // Note: GetEnumerator() and EnumerateRunes() allocate a temporary managed string.
  // For zero-alloc character iteration use AsSpan() directly with a foreach.
  public RCArray<char>.Enumerator GetEnumerator() => this.chars.GetEnumerator();
  IEnumerator<char> IEnumerable<char>.GetEnumerator() => GetEnumerator();
  IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

  public SpanRuneEnumerator EnumerateRunes() => new(++this);



  public struct SpanRuneEnumerator : IEnumerator<Rune>
  {
    private RCString str;
    private int index;

    public SpanRuneEnumerator(RCString span)
    {
      this.str = span;
      this.index = 0;
    }

    private Rune current;
    public Rune Current => current;
    object IEnumerator.Current => Current;

    public void Dispose()
    {
      str.Dispose();
      Reset();
    }

    public bool MoveNext()
    {
      if (str.IsEmpty)
      {
        return false;
      }

      // Rune.DecodeFromUtf16 is the magic sauce here.
      // It handles surrogate pairs correctly without needing a System.String.
      OperationStatus status = Rune.DecodeFromUtf16(str, out current, out int charsConsumed);

      if (status == OperationStatus.Done)
      {
        this.index += charsConsumed;
        return true;
      }

      // If data is invalid/truncated, .NET convention is to return the Unicode Replacement Char (U+FFFD)
      current = Rune.ReplacementChar;
      this.index++;
      return true;
    }

    public void Reset()
    {
      this.index = 0;
    }
  }



  public ref struct ValueListBuilder<T>
  {
    private Span<T> _span;
    private T[]? _arrayFromPool;
    private int _pos;

    public ValueListBuilder(Span<T> initialSpan)
    {
      _span = initialSpan;
      _arrayFromPool = null;
      _pos = 0;
    }

    public int Length => _pos;

    public Span<T> AsSpan() => _span.Slice(0, _pos);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Append(T item)
    {
      if (_pos >= _span.Length)
      {
        Grow();
      }

      _span[_pos++] = item;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Grow()
    {
      // Double the capacity, or start at 4 if somehow empty
      int newCapacity = _span.Length == 0 ? 4 : _span.Length * 2;

      T[] array = ArrayPool<T>.Shared.Rent(newCapacity);
      _span.CopyTo(array);

      T[]? oldArray = _arrayFromPool;
      _span = _arrayFromPool = array;

      if (oldArray != null)
      {
        ArrayPool<T>.Shared.Return(oldArray);
      }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
      T[]? toReturn = _arrayFromPool;
      if (toReturn != null)
      {
        _arrayFromPool = null;
        ArrayPool<T>.Shared.Return(toReturn);
      }
    }
  }
}