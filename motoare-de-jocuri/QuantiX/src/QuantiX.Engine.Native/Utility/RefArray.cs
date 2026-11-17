using System.Collections;
using System.Runtime.CompilerServices;
using QuantiX.Engine.Native.Memory;

namespace QuantiX.Engine.Native.Utility;

public readonly struct RefArray<T> : IRefCounted<RefArray<T>>, ICollection<T>
  where T : unmanaged
{
  private readonly RefBuffer buffer;
  public RefArray(long length)
  {
    unsafe
    {
      this.buffer = new(length * sizeof(T));
      *(long*)this.buffer.Data = length;
    }
  }

  public unsafe int Length => *(int*)this.buffer.Data;
  public unsafe long LongLength => *(long*)this.buffer.Data;

  public int Count => Length;
  public bool IsReadOnly => true;

  private unsafe void* ArrayStart => (void*)((nint)this.buffer.Data + sizeof(long));

  public static RefArray<T> Null => default;

  public void Add(T item)
  {
    throw new NotSupportedException();
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

  public Enumerator<T> GetEnumerator()
  {
    return new(this);
  }

  IEnumerator<T> IEnumerable<T>.GetEnumerator()
  {
    throw new NotImplementedException();
  }

  public bool Remove(T item)
  {
    throw new NotImplementedException();
  }

  IEnumerator IEnumerable.GetEnumerator()
  {
    return GetEnumerator();
  }

  public RefArray<T> Pass()
  {
    this.buffer.Pass();
    return this;
  }

  public void Dispose()
  {
    this.buffer.Dispose();
  }

  public bool Equals(RefArray<T> other) => this.buffer == other.buffer;
  public override bool Equals(object? other) => this.buffer.Equals(other);
  public override int GetHashCode() => this.buffer.GetHashCode();



  public unsafe ref T this[long index]
  {
    get
    {
      if (index < 0 || index >= Length)
      {
        throw new IndexOutOfRangeException($"Index {{{index}}} is not in range {{ {0}, {Length - 1} }}");
      }
      return ref ((T*)ArrayStart)[index];
    }
  }
  public static bool operator ==(RefArray<T> left, RefArray<T> right) => left.Equals(right);
  public static bool operator !=(RefArray<T> left, RefArray<T> right) => !left.Equals(right);



  public struct Enumerator<TVal> : IEnumerator<TVal>
    where TVal : unmanaged
  {
    private readonly RefArray<TVal> array;
    private long i;
    internal Enumerator(RefArray<TVal> array)
    {
      this.array = array.Pass();
      this.i = 0;
    }

    public TVal Current => this.array[i];
    object IEnumerator.Current => Current;

    public void Dispose()
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