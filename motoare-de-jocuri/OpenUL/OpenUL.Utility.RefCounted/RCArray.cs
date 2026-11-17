using System.Collections;

namespace OpenUL.Utility.RefCounted;

public class RCArray<T> : RCObject, ICollection<T>, IEnumerable<T>, IList<T>, ICloneable
{
  private T[] Array;
  public RCArray(sbyte length)
  {
    Array = new T[length];
  }

  public RCArray(short length)
  {
    Array = new T[length];
  }

  public RCArray(int length)
  {
    Array = new T[length];
  }

  public RCArray(long length)
  {
    Array = new T[length];
  }

  public RCArray(byte length)
  {
    Array = new T[length];
  }

  public RCArray(ushort length)
  {
    Array = new T[length];
  }

  public RCArray(uint length)
  {
    Array = new T[length];
  }

  public RCArray(ulong length)
  {
    Array = new T[length];
  }

  public RCArray(T[] values)
  : this(values, 0, values.LongLength) { }

  public RCArray(T[] values, long start)
  : this(values, start, values.LongLength) { }

  public RCArray(T[] values, long start, long length)
  {
    if ((uint)length > (uint)values.Length || (uint)start > (uint)values.Length)
    {
      throw new IndexOutOfRangeException();
    }
    var arr = new T[length];
    for (long i = start; i < length; i++)
    {
      arr[i] = values[i];
    }
    Array = arr;
  }

  public RCArray(IEnumerable<T> values)
  {
    var arr = new T[values.LongCount()];
    long i = 0;
    foreach (var elem in values)
    {
      arr[i] = elem;
      i++;
    }
    Array = arr;
  }

  public RCArray(IList<T> values)
  {
    var arr = new T[values.Count];
    for (int i = 0; i < values.Count; i++)
    {
      arr[i] = values[i];
    }
    Array = arr;
  }

  public RCArray(ReadOnlySpan<T> values)
  {
    var arr = new T[values.Length];
    for (int i = 0; i < values.Length; i++)
    {
      arr[i] = values[i];
    }
    Array = arr;
  }


  protected override void OnTerminate()
  {
    base.OnTerminate();
    if (Array is RCObject[] rcArray)
    {
      for (long i = 0; i < rcArray.Length; i++)
      {
        rcArray[i].Dispose();
      }
      return;
    }
    for (long i = 0; i < Array.Length; i++)
    {
      if (Array[i] is RCObject obj)
      {
        obj.Dispose();
      }
    }
  }


  public int Length => Array.Length;
  public long LongLength => Array.Length;
  int ICollection<T>.Count => Array.Length;

  public bool IsReadOnly => false;

  public ref T this[sbyte index] => ref Array[index];
  public ref T this[short index] => ref Array[index];
  public ref T this[int index] => ref Array[index];
  public ref T this[long index] => ref Array[index];
  public ref T this[byte index] => ref Array[index];
  public ref T this[ushort index] => ref Array[index];
  public ref T this[uint index] => ref Array[index];
  public ref T this[ulong index] => ref Array[index];
  public ref T this[Index index] => ref Array[index];

  T IList<T>.this[int index]
  {
    get => this[index];
    set => this[index] = value;
  }

  public void Add(T item)
    => throw new InvalidOperationException();
  public void Clear()
    => throw new InvalidOperationException();
  public bool Remove(T item)
    => throw new InvalidOperationException();



  public bool Contains(T item)
    => Array.Contains(item);
  public void CopyTo(T[] array, int arrayIndex)
    => Array.AsSpan().CopyTo(array.AsSpan(arrayIndex));


  public Enumerator GetEnumerator()
    => new(Array);
  IEnumerator<T> IEnumerable<T>.GetEnumerator()
    => GetEnumerator();
  IEnumerator IEnumerable.GetEnumerator()
    => GetEnumerator();


  public object Clone()
  {
    return new RCArray<T>(Array);
  }

  public int IndexOf(T item)
    => Array.IndexOf(item);
  public void Insert(int index, T item)
    => Array[index] = item;
  public void RemoveAt(int index)
    => throw new InvalidOperationException();


  public struct Enumerator : IEnumerator<T>
  {
    private T[] Array;
    private long Index;
    public Enumerator(T[] array)
    {
      Array = array;
    }

    public readonly T Current => Array[Index];

    readonly object? IEnumerator.Current => Current;

    public void Dispose()
    {
      Reset();
    }

    public bool MoveNext()
    {
      bool result = Index < Array.LongLength;
      Index++;
      return result;
    }

    public void Reset()
    {
      Index = 0;
    }
  }
}


