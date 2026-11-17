using System.Collections;
using System.Runtime.CompilerServices;

namespace UnivKit.Unmanaged.RefCounted;

[CollectionBuilder(typeof(RCArray), nameof(RCArray.Create))]
public struct RCArray<T> : IRefCounted<RCArray<T>>, IList<T>
  where T : unmanaged
{
  private RCBuffer buffer;
  public RCArray(long length)
  {
    unsafe
    {
      this.buffer = new(sizeof(long) + length * sizeof(T));
      *(long*)this.buffer.Data = length;
    }
  }

  public readonly unsafe int Length => *(int*)this.buffer.Data;
  public readonly long RefCount => this.buffer.RefCount;
  public readonly unsafe long LongLength => *(long*)this.buffer.Data;

  public int Count => Length;
  public readonly bool IsReadOnly => true;

  private readonly unsafe void* ArrayStart => (void*)((nint)this.buffer.Data + sizeof(long));

  void ICollection<T>.Add(T item)
  {
    throw new NotSupportedException();
  }

  bool ICollection<T>.Remove(T item)
  {
    throw new NotImplementedException();
  }

  public void Clear()
  {
    unsafe
    {
      Unsafe.InitBlock(ArrayStart, 0, (uint)(Length * sizeof(T)));
    }
  }

  public bool Contains(T item)
  {
    for (int i = 0; i < Length; i++)
    {
      if (EqualityComparer<T>.Default.Equals(this[i], item))
      {
        return true;
      }
    }
    return false;
  }

  public void CopyTo(T[] array, int arrayIndex)
  {
    for (int i = 0, j = arrayIndex; i < Length && j < array.Length; i++, j++)
    {
      array[j] = this[i];
    }
  }



  IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
  IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

  public Enumerator GetEnumerator()
  {
    return new(++this);
  }


  public static RCArray<T> operator ++(RCArray<T> o)
  {
    o.buffer++;
    return o;
  }
  public static RCArray<T> operator --(RCArray<T> o)
  {
    o.buffer--;
    return o;
  }

  public void Dispose()
  {
    if (this.RefCount > 1) return;
    for (int i = 0; i < Length; i++)
    {
      if (this[i] is IDisposable disposable)
      {
        disposable.Dispose();
      }
    }
    this--;
  }

  public readonly bool Equals(RCArray<T> other) => this.buffer == other.buffer;
  public override bool Equals(object? other) => this.buffer.Equals(other);
  public override int GetHashCode() => this.buffer.GetHashCode();



  public unsafe Span<T> AsSpan(int start, int length)
  {
    ThrowIfNotInBounds(start);
    if (length > Length)
    {
      throw new ArgumentOutOfRangeException(nameof(length), length, "Specified length is outside the bounds of the array");
    }

    return new Span<T>(&((T*)this.ArrayStart)[start], length);
  }

  public unsafe Span<T> AsSpan(int start)
  {
    ThrowIfNotInBounds(start);
    return new Span<T>(&((T*)this.ArrayStart)[start], Length - start);
  }

  public unsafe Span<T> AsSpan()
  {
    return new Span<T>(this.ArrayStart, Length);
  }

  public Span<T> AsSpan(Range range)
  {
    (int offset, int length) = range.GetOffsetAndLength(Length);
    return AsSpan(offset, length);
  }

  public Span<T> AsSpan(Index range)
  {
    int offset = range.GetOffset(Length);
    return AsSpan(offset);
  }



  public unsafe ref T this[long index]
  {
    get
    {
      ThrowIfNotInBounds(index);
      return ref ((T*)ArrayStart)[index];
    }
  }

  public unsafe ref T this[int index]
  {
    get
    {
      ThrowIfNotInBounds(index);
      return ref ((T*)ArrayStart)[index];
    }
  }

  public unsafe ref T this[Index index]
  {
    get
    {
      int val = index.GetOffset(Length);
      ThrowIfNotInBounds(val);
      return ref ((T*)ArrayStart)[val];
    }
  }

  T IList<T>.this[int index]
  {
    get => this[index];
    set => this[index] = value;
  }

  public static bool operator ==(RCArray<T> left, RCArray<T> right) => left.Equals(right);
  public static bool operator !=(RCArray<T> left, RCArray<T> right) => !left.Equals(right);



  public int IndexOf(T item)
  {
    for (int i = 0; i < Length; i++)
    {
      if (this[i].Equals(item))
      {
        return i;
      }
    }
    return -1;
  }

  public void Insert(int index, T item)
  {
    this[index] = item;
  }

  public void RemoveAt(int index)
  {
    this[index] = default;
  }



  private void ThrowIfNotInBounds(long index)
  {
    if (index < 0 || index >= Length)
    {
      throw new IndexOutOfRangeException($"Index {{{index}}} is not in range {{ {0}, {Length - 1} }}");
    }
  }



  public struct Enumerator : IEnumerator<T>
  {
    private readonly RCArray<T> array;
    private long i;
    internal Enumerator(RCArray<T> array)
    {
      this.array = array;
      this.i = -1;
    }

    public readonly T Current => this.array[i];
    object IEnumerator.Current => Current;

    public readonly void Dispose()
    {
      this.array.Dispose();
    }

    public bool MoveNext()
    {
      i++;
      return i < this.array.Length;
    }

    public void Reset()
    {
      i = 0;
    }
  }
}

public static class RCArray
{
  public static RCArray<T> Create<T>(ReadOnlySpan<T> values)
    where T : unmanaged
  {
    RCArray<T> arr = new(values.Length);
    for (int i = 0; i < values.Length; i++)
    {
      arr[i] = values[i];
    }
    return arr;
  }

  public static RCArray<T> ToRCArray<T>(this RCArray<T> array)
    where T : unmanaged
  {
    return array;
  }

  public static RCArray<T> ToRCArray<T>(this IEnumerable<T> enumerable)
    where T : unmanaged
  {
    if (enumerable is RCArray<T> rcArray)
    {
      return rcArray;
    }
    if (enumerable is ICollection<T> col)
    {
      RCArray<T> result = new(col.Count);
      int i = 0;
      foreach (var item in col)
      {
        result[i] = item;
        i++;
      }
      return result;
    }

    {
      var arr = enumerable.ToArray();
      RCArray<T> result = new(arr.Length);
      int i = 0;
      foreach (var item in arr)
      {
        result[i] = item;
        i++;
      }
      return result;
    }
  }
}